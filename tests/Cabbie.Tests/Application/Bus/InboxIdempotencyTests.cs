using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Persistence;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Infrastructure.Persistence;
using Cabbie.Tests.Application.Transports;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Bus;

public sealed class InboxIdempotencyTests
{
    public InboxIdempotencyTests() => InboxLog.Ran.Clear();

    [Fact]
    public async Task duplicate_delivery_of_one_envelope_runs_the_handler_once()
    {
        MessageDelivery delivery = DeliveryFor([typeof(CountingHandler)], new InMemoryMessageStore().Inbox);
        Envelope envelope = EnvelopeFor(new InboxPing(1));

        await delivery.Dispatch(envelope, scheduled: true);
        await delivery.Dispatch(envelope, scheduled: true);

        Assert.Equal(["counting"], InboxLog.Ran);
    }

    [Fact]
    public async Task distinct_envelope_ids_each_run_the_handler()
    {
        MessageDelivery delivery = DeliveryFor([typeof(CountingHandler)], new InMemoryMessageStore().Inbox);

        await delivery.Dispatch(EnvelopeFor(new InboxPing(1)), scheduled: true);
        await delivery.Dispatch(EnvelopeFor(new InboxPing(1)), scheduled: true);

        Assert.Equal(["counting", "counting"], InboxLog.Ran);
    }

    [Fact]
    public async Task without_an_inbox_duplicate_delivery_runs_the_handler_again()
    {
        MessageDelivery delivery = DeliveryFor([typeof(CountingHandler)], inbox: null);
        Envelope envelope = EnvelopeFor(new InboxPing(1));

        await delivery.Dispatch(envelope, scheduled: true);
        await delivery.Dispatch(envelope, scheduled: true);

        Assert.Equal(["counting", "counting"], InboxLog.Ran);
    }

    [Fact]
    public async Task redelivery_after_one_handler_failed_re_runs_only_the_failed_handler()
    {
        InboxFlaky.FailNext = true;
        MessageDelivery delivery = DeliveryFor([typeof(CountingHandler), typeof(FlakyHandler)], new InMemoryMessageStore().Inbox);
        Envelope envelope = EnvelopeFor(new InboxPing(1));

        await Assert.ThrowsAsync<HandlerFault>(() => delivery.Dispatch(envelope, scheduled: false));
        Assert.Equal(["counting", "flaky"], InboxLog.Ran);

        InboxLog.Ran.Clear();
        await delivery.Dispatch(envelope, scheduled: false);

        Assert.Equal(["flaky"], InboxLog.Ran);
    }

    [Fact]
    public async Task envelope_targeted_at_one_handler_does_not_mark_the_other_handlers_done()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(CountingHandler));
        catalog.Scan(typeof(FlakyHandler));
        IInboxStore inbox = new InMemoryMessageStore().Inbox;
        var delivery = new MessageDelivery(catalog, new Executor(new ErrorPolicyCatalog()), inbox: inbox);
        DiscoveredHandler flaky = catalog.Handlers.Single(handler => handler.HandlerType == typeof(FlakyHandler));
        Envelope envelope = EnvelopeFor(new InboxPing(1));

        await delivery.Dispatch(envelope with { Handler = new HandlerName(flaky.Name) }, scheduled: true);
        await delivery.Dispatch(envelope, scheduled: true);

        Assert.Equal(["flaky", "counting"], InboxLog.Ran);
    }

    [Fact]
    public async Task cascade_send_failure_after_handler_success_does_not_re_run_the_handler_on_redelivery()
    {
        var sender = new FlakySender { FailNext = true };
        MessageDelivery delivery = DeliveryFor([typeof(CascadingHandler)], new InMemoryMessageStore().Inbox, sender);
        Envelope envelope = EnvelopeFor(new InboxPing(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => delivery.Dispatch(envelope, scheduled: true));
        Assert.Empty(sender.Sent);

        await delivery.Dispatch(envelope, scheduled: true);

        Assert.Equal(["cascading"], InboxLog.Ran);
        Envelope cascade = Assert.Single(sender.Sent);
        Assert.IsType<InboxPong>(cascade.Message.Value);
    }

    [Fact]
    public async Task cascades_are_sent_once_when_redelivery_follows_a_successful_send()
    {
        var sender = new FlakySender();
        MessageDelivery delivery = DeliveryFor([typeof(CascadingHandler)], new InMemoryMessageStore().Inbox, sender);
        Envelope envelope = EnvelopeFor(new InboxPing(1));

        await delivery.Dispatch(envelope, scheduled: true);
        await delivery.Dispatch(envelope, scheduled: true);

        Assert.Equal(["cascading"], InboxLog.Ran);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task requeued_attempt_is_not_deduplicated_against_the_failed_one()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<InvalidOperationException>().Requeue();
        var sender = new RecordingSender();
        InboxFlaky.FailNext = true;
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(FlakyHandler));
        var delivery = new MessageDelivery(
            catalog,
            new Executor(policies, sender: () => sender),
            inbox: new InMemoryMessageStore().Inbox);

        await delivery.Dispatch(EnvelopeFor(new InboxPing(1)), scheduled: true);
        Envelope requeued = Assert.Single(sender.Sent);
        InboxLog.Ran.Clear();
        await delivery.Dispatch(requeued, scheduled: true);

        Assert.Equal(["flaky"], InboxLog.Ran);
    }

    [Fact]
    public async Task two_consumers_acquiring_the_same_id_and_handler_yield_one_winner()
    {
        IInboxStore inbox = new InMemoryMessageStore().Inbox;
        var id = new EnvelopeId(Guid.NewGuid());
        var handler = new HandlerName("orders.handle");

        bool[] results = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => Task.Run(async () => await inbox.TryAcquireAsync(id, handler))));

        Assert.Single(results, won => won);
    }

    [Fact]
    public async Task same_id_acquired_by_different_handlers_is_tracked_independently()
    {
        IInboxStore inbox = new InMemoryMessageStore().Inbox;
        var id = new EnvelopeId(Guid.NewGuid());

        Assert.True(await inbox.TryAcquireAsync(id, new HandlerName("a")));
        Assert.True(await inbox.TryAcquireAsync(id, new HandlerName("b")));
        await inbox.MarkProcessedAsync(id, new HandlerName("a"));

        Assert.False(await inbox.TryAcquireAsync(id, new HandlerName("a")));
        Assert.Equal(new InboxEntry(id, new HandlerName("b")), Assert.Single(await inbox.LoadInFlightAsync()));
    }

    private static MessageDelivery DeliveryFor(Type[] handlers, IInboxStore? inbox, ISender? sender = null)
    {
        var catalog = new HandlerCatalog();
        foreach (Type handler in handlers)
        {
            catalog.Scan(handler);
        }

        return new MessageDelivery(catalog, new Executor(new ErrorPolicyCatalog()), sender: sender, inbox: inbox);
    }

    private static Envelope EnvelopeFor(object message) =>
        EnvelopeFactory.Create(
            message: new Message(message),
            messageType: MessageTypeNaming.For(message.GetType()));
}

internal sealed class FlakySender : ISender
{
    public bool FailNext { get; set; }

    public List<Envelope> Sent { get; } = [];

    public ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new InvalidOperationException("transport down");
        }

        Sent.Add(envelope);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class InboxLog
{
    public static List<string> Ran { get; } = [];
}

internal static class InboxFlaky
{
    public static bool FailNext { get; set; }
}

public sealed record InboxPing(int Id);

public sealed record InboxPong(int Id);

public sealed class CountingHandler
{
    public void Handle(InboxPing message) => InboxLog.Ran.Add("counting");
}

public sealed class FlakyHandler
{
    public void Handle(InboxPing message)
    {
        InboxLog.Ran.Add("flaky");
        if (InboxFlaky.FailNext)
        {
            InboxFlaky.FailNext = false;
            throw new InvalidOperationException("boom");
        }
    }
}

public sealed class CascadingHandler
{
    public InboxPong Handle(InboxPing message)
    {
        InboxLog.Ran.Add("cascading");
        return new InboxPong(message.Id);
    }
}
