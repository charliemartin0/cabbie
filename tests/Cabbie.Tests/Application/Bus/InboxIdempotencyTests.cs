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
    public async Task requeued_attempt_delivered_before_the_original_returns_is_not_dropped()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<InvalidOperationException>().Requeue();
        var sender = new ImmediateRedeliverySender();
        InboxFlaky.FailNext = true;
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(FlakyHandler));
        var delivery = new MessageDelivery(
            catalog,
            new Executor(policies, sender: () => sender),
            inbox: new InMemoryMessageStore().Inbox);
        sender.Delivery = delivery;

        await delivery.Dispatch(EnvelopeFor(new InboxPing(1)), scheduled: true);

        Assert.Equal(["flaky", "flaky"], InboxLog.Ran);
    }

    [Fact]
    public async Task failed_outbox_commit_leaves_the_handler_unprocessed_for_redelivery()
    {
        var transaction = new CommitFailingTransaction { FailCommit = true };
        InboxTx.Next = transaction;
        MessageDelivery delivery = DeliveryFor([typeof(TransactionalHandler)], new InMemoryMessageStore().Inbox);
        Envelope envelope = EnvelopeFor(new InboxPing(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => delivery.Dispatch(envelope, scheduled: true));
        transaction.FailCommit = false;
        InboxTx.Next = transaction = new CommitFailingTransaction();
        await delivery.Dispatch(envelope, scheduled: true);

        Assert.Equal(["transactional", "transactional"], InboxLog.Ran);
        Assert.True(transaction.Committed);
    }

    [Fact]
    public async Task committed_outbox_transaction_marks_the_handler_processed()
    {
        InboxTx.Next = new CommitFailingTransaction();
        MessageDelivery delivery = DeliveryFor([typeof(TransactionalHandler)], new InMemoryMessageStore().Inbox);
        Envelope envelope = EnvelopeFor(new InboxPing(1));

        await delivery.Dispatch(envelope, scheduled: true);
        await delivery.Dispatch(envelope, scheduled: true);

        Assert.Equal(["transactional"], InboxLog.Ran);
    }

    [Fact]
    public async Task in_process_invoke_never_touches_the_inbox_but_transport_delivery_does()
    {
        var inbox = new RecordingInbox();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(CountingHandler));
        var mediator = new Cabbie.Application.Mediator.Mediator(catalog, inbox: inbox);

        await mediator.InvokeAsync(new InboxPing(1));
        Assert.Equal(0, inbox.Acquired);

        await mediator.DispatchHandlers(EnvelopeFor(new InboxPing(1)), scheduled: true, CancellationToken.None, fromTransport: true);
        Assert.Equal(1, inbox.Acquired);
    }

    [Fact]
    public async Task processed_entries_are_forgotten_after_the_retention_window()
    {
        var time = new ManualTimeProvider();
        IInboxStore inbox = new InMemoryMessageStore(time, inboxRetention: TimeSpan.FromMinutes(10)).Inbox;
        var id = new EnvelopeId(Guid.NewGuid());
        var handler = new HandlerName("h");
        Assert.True(await inbox.TryAcquireAsync(id, handler));
        await inbox.MarkProcessedAsync(id, handler);

        time.Advance(TimeSpan.FromMinutes(5));
        Assert.False(await inbox.TryAcquireAsync(id, handler));

        time.Advance(TimeSpan.FromMinutes(6));
        Assert.True(await inbox.TryAcquireAsync(id, handler));
    }

    [Fact]
    public async Task oldest_processed_entries_are_evicted_beyond_capacity()
    {
        IInboxStore inbox = new InMemoryMessageStore(inboxCapacity: 2).Inbox;
        var handler = new HandlerName("h");
        EnvelopeId[] ids = [new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid())];
        foreach (EnvelopeId id in ids)
        {
            Assert.True(await inbox.TryAcquireAsync(id, handler));
            await inbox.MarkProcessedAsync(id, handler);
        }

        Assert.True(await inbox.TryAcquireAsync(ids[0], handler));
        Assert.False(await inbox.TryAcquireAsync(ids[2], handler));
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

internal sealed class ImmediateRedeliverySender : ISender
{
    public MessageDelivery? Delivery { get; set; }

    public async ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default) =>
        await Delivery!.Dispatch(envelope, scheduled: true, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class InboxTx
{
    public static IOutboxTransaction? Next { get; set; }
}

internal sealed class CommitFailingTransaction : IOutboxTransaction
{
    public bool FailCommit { get; set; }

    public bool Committed { get; private set; }

    public ValueTask StageAsync(Envelope envelope, CancellationToken ct = default) => ValueTask.CompletedTask;

    public ValueTask CommitAsync(CancellationToken ct = default)
    {
        if (FailCommit)
        {
            throw new InvalidOperationException("commit failed");
        }

        Committed = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask RollbackAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}

internal sealed class RecordingInbox : IInboxStore
{
    public int Acquired { get; private set; }

    public ValueTask<bool> TryAcquireAsync(EnvelopeId id, HandlerName handler, CancellationToken ct = default)
    {
        Acquired++;
        return ValueTask.FromResult(true);
    }

    public ValueTask MarkProcessedAsync(EnvelopeId id, HandlerName handler, CancellationToken ct = default) =>
        ValueTask.CompletedTask;

    public ValueTask ReleaseAsync(EnvelopeId id, HandlerName handler, CancellationToken ct = default) =>
        ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<InboxEntry>> LoadInFlightAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<InboxEntry>>([]);
}

public sealed class TransactionalHandler
{
    public void Handle(InboxPing message, Envelope envelope)
    {
        InboxLog.Ran.Add("transactional");
        OutboxTransactionScope.Register(envelope.Id, InboxTx.Next!);
    }
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
