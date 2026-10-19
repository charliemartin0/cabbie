using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Tests.Application.Execution;
using Cabbie.Tests.Application.Transports;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Bus;

public sealed class FanOutIsolationTests
{
    public FanOutIsolationTests() => FanOutLog.Ran.Clear();

    [Fact]
    public async Task fan_out_with_one_failing_handler_runs_the_others_exactly_once()
    {
        MessageDelivery delivery = DeliveryFor(new ErrorPolicyCatalog());

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => delivery.Dispatch(ChargePaymentEnvelope(), scheduled: true));

        Assert.IsType<ArgumentException>(fault.InnerException);
        Assert.Equal(["first", "unresolved", "last"], FanOutLog.Ran);
    }

    [Fact]
    public async Task dead_letter_of_one_handler_leaves_others_outcomes_intact()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<InvalidOperationException>().MoveToErrorQueue();
        var errorQueue = new RecordingErrorQueue();
        MessageDelivery delivery = DeliveryFor(policies, errorQueue, handlers: [typeof(FirstOkHandler), typeof(DeadLetterHandler), typeof(LastOkHandler)]);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => delivery.Dispatch(ChargePaymentEnvelope(), scheduled: true));

        Assert.True(fault.DeadLettered);
        Assert.Single(errorQueue.Moved);
        Assert.Equal(["first", "dead-letter", "last"], FanOutLog.Ran);
    }

    [Fact]
    public async Task dead_lettered_fault_does_not_mask_an_unresolved_fault_in_another_handler()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<InvalidOperationException>().MoveToErrorQueue();
        MessageDelivery delivery = DeliveryFor(
            policies,
            new RecordingErrorQueue(),
            handlers: [typeof(DeadLetterHandler), typeof(UnresolvedHandler)]);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => delivery.Dispatch(ChargePaymentEnvelope(), scheduled: true));

        Assert.False(fault.DeadLettered);
        Assert.Equal(["dead-letter", "unresolved"], FanOutLog.Ran);
    }

    [Fact]
    public async Task envelope_targeted_at_one_handler_runs_only_that_handler()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(FirstOkHandler));
        catalog.Scan(typeof(LastOkHandler));
        var delivery = new MessageDelivery(catalog, new Executor(new ErrorPolicyCatalog()));
        DiscoveredHandler last = catalog.Handlers.Single(handler => handler.HandlerType == typeof(LastOkHandler));

        await delivery.Dispatch(ChargePaymentEnvelope() with { Handler = new HandlerName(last.Name) }, scheduled: true);

        Assert.Equal(["last"], FanOutLog.Ran);
    }

    [Fact]
    public async Task envelope_targeting_an_unregistered_handler_goes_to_the_missing_handler()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(FirstOkHandler));
        var missing = new RecordingMissingHandler();
        var delivery = new MessageDelivery(
            catalog,
            new Executor(new ErrorPolicyCatalog(), missingHandler: missing));

        await delivery.Dispatch(ChargePaymentEnvelope() with { Handler = new HandlerName("gone.Handle") }, scheduled: true);

        Assert.Empty(FanOutLog.Ran);
        Assert.Single(missing.Seen);
    }

    [Fact]
    public async Task requeue_of_one_handler_acks_and_redelivery_re_runs_only_that_handler()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<InvalidOperationException>().Requeue();
        var sender = new RecordingSender();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(FirstOkHandler));
        catalog.Scan(typeof(DeadLetterHandler));
        catalog.Scan(typeof(LastOkHandler));
        var receiver = new EnvelopeReceiver(
            new MessageDelivery(catalog, new Executor(policies, sender: () => sender)));

        ReceiveOutcome first = await receiver.ReceiveAsync(ChargePaymentEnvelope(), CancellationToken.None);

        Assert.Equal(ReceiveOutcome.Ack, first);
        Assert.Equal(["first", "dead-letter", "last"], FanOutLog.Ran);
        Envelope requeued = Assert.Single(sender.Sent);
        Assert.Equal(2, requeued.Attempts.Value);

        FanOutLog.Ran.Clear();
        await receiver.ReceiveAsync(requeued, CancellationToken.None);

        Assert.Equal(["dead-letter"], FanOutLog.Ran);
    }

    [Fact]
    public async Task requeueing_handler_runs_once_when_another_handler_faults_unresolved()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<InvalidOperationException>().Requeue();
        var sender = new RecordingSender();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(DeadLetterHandler));
        catalog.Scan(typeof(UnresolvedHandler));
        var receiver = new EnvelopeReceiver(
            new MessageDelivery(catalog, new Executor(policies, sender: () => sender)));

        ReceiveOutcome first = await receiver.ReceiveAsync(ChargePaymentEnvelope(), CancellationToken.None);

        Assert.Equal(ReceiveOutcome.Ack, first);
        Assert.Single(FanOutLog.Ran, "dead-letter");
        Assert.Equal(2, sender.Sent.Count);
        Assert.All(sender.Sent, sent => Assert.Equal(2, sent.Attempts.Value));
        Assert.Equal(2, sender.Sent.Select(sent => sent.Handler).Distinct().Count());

        foreach (Envelope redelivered in sender.Sent.ToList())
        {
            FanOutLog.Ran.Clear();
            await receiver.ReceiveAsync(redelivered, CancellationToken.None);
            string expected = redelivered.Handler!.Value.Contains("DeadLetterHandler", StringComparison.Ordinal)
                ? "dead-letter"
                : "unresolved";
            Assert.All(FanOutLog.Ran, name => Assert.Equal(expected, name));
            Assert.NotEmpty(FanOutLog.Ran);
        }
    }

    private static MessageDelivery DeliveryFor(
        ErrorPolicyCatalog policies,
        RecordingErrorQueue? errorQueue = null,
        Type[]? handlers = null)
    {
        policies.OnUnmatchedException();
        var catalog = new HandlerCatalog();
        foreach (Type handler in handlers ?? [typeof(FirstOkHandler), typeof(UnresolvedHandler), typeof(LastOkHandler)])
        {
            catalog.Scan(handler);
        }

        return new MessageDelivery(catalog, new Executor(policies, errorQueue: errorQueue));
    }

    private static Envelope ChargePaymentEnvelope() =>
        EnvelopeFactory.Create(
            message: new Message(new ChargePayment(1)),
            messageType: MessageTypeNaming.For(typeof(ChargePayment)));
}

internal sealed class RecordingMissingHandler : IMissingHandler
{
    public List<Envelope> Seen { get; } = [];

    public Task HandleAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        Seen.Add(envelope);
        return Task.CompletedTask;
    }
}

internal static class FanOutLog
{
    public static List<string> Ran { get; } = [];
}

public sealed class FirstOkHandler
{
    public void Handle(ChargePayment message) => FanOutLog.Ran.Add("first");
}

public sealed class UnresolvedHandler
{
    public void Handle(ChargePayment message)
    {
        FanOutLog.Ran.Add("unresolved");
        throw new ArgumentException("boom");
    }
}

public sealed class DeadLetterHandler
{
    public void Handle(ChargePayment message)
    {
        FanOutLog.Ran.Add("dead-letter");
        throw new InvalidOperationException("boom");
    }
}

public sealed class LastOkHandler
{
    public void Handle(ChargePayment message) => FanOutLog.Ran.Add("last");
}
