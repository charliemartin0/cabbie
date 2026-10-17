using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Tests.Application.Execution;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Transports;

public sealed class EnvelopeReceiverTests
{
    [Fact]
    public async Task ack_is_returned_only_after_the_handler_finishes()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GatedPaymentHandler.Gate = gate;
        EnvelopeReceiver receiver = ReceiverFor<GatedPaymentHandler>(new Executor(new ErrorPolicyCatalog()));

        Task<ReceiveOutcome> outcome = receiver.ReceiveAsync(ChargePaymentEnvelope(), CancellationToken.None);
        await Task.Delay(100);

        Assert.False(outcome.IsCompleted);
        gate.SetResult();
        Assert.Equal(ReceiveOutcome.Ack, await outcome.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task fault_dead_lettered_by_policy_is_acked_and_the_envelope_is_in_the_error_queue()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<InvalidOperationException>().MoveToErrorQueue();
        var errorQueue = new RecordingErrorQueue();
        EnvelopeReceiver receiver = ReceiverFor<ThrowingPaymentHandler>(new Executor(policies, errorQueue: errorQueue));
        Envelope envelope = ChargePaymentEnvelope();

        ReceiveOutcome outcome = await receiver.ReceiveAsync(envelope, CancellationToken.None);

        Assert.Equal(ReceiveOutcome.Ack, outcome);
        Assert.Equal(envelope.Id, Assert.Single(errorQueue.Moved).Id);
    }

    [Fact]
    public async Task fault_requeued_by_policy_is_acked_and_the_envelope_is_sent_to_its_destination_again()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<InvalidOperationException>().Requeue();
        var sender = new RecordingSender();
        EnvelopeReceiver receiver = ReceiverFor<ThrowingPaymentHandler>(new Executor(policies, sender: () => sender));
        Envelope envelope = ChargePaymentEnvelope();

        ReceiveOutcome outcome = await receiver.ReceiveAsync(envelope, CancellationToken.None);

        Assert.Equal(ReceiveOutcome.Ack, outcome);
        Envelope resent = Assert.Single(sender.Sent);
        Assert.Equal(envelope.Id, resent.Id);
        Assert.Equal(envelope.Destination, resent.Destination);
    }

    [Fact]
    public async Task fault_the_policy_does_not_resolve_is_nacked()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnUnmatchedException();
        var errorQueue = new RecordingErrorQueue();
        EnvelopeReceiver receiver = ReceiverFor<ThrowingPaymentHandler>(new Executor(policies, errorQueue: errorQueue));

        ReceiveOutcome outcome = await receiver.ReceiveAsync(ChargePaymentEnvelope(), CancellationToken.None);

        Assert.Equal(ReceiveOutcome.Nack, outcome);
        Assert.Empty(errorQueue.Moved);
    }

    [Fact]
    public async Task receive_cancelled_before_the_handler_finishes_is_deferred()
    {
        using var cts = new CancellationTokenSource();
        EnvelopeReceiver receiver = ReceiverFor<WaitsForCancellationHandler>(new Executor(new ErrorPolicyCatalog()));

        Task<ReceiveOutcome> outcome = receiver.ReceiveAsync(ChargePaymentEnvelope(), cts.Token);
        await cts.CancelAsync();

        Assert.Equal(ReceiveOutcome.Defer, await outcome.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static EnvelopeReceiver ReceiverFor<THandler>(Executor executor)
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(THandler));
        return new EnvelopeReceiver(new MessageDelivery(catalog, executor));
    }

    private static Envelope ChargePaymentEnvelope() =>
        EnvelopeFactory.Create(
            message: new Message(new ChargePayment(1)),
            messageType: MessageTypeNaming.For(typeof(ChargePayment)));
}

public sealed class GatedPaymentHandler
{
    public static TaskCompletionSource? Gate { get; set; }

    public Task Handle(ChargePayment message) => Gate!.Task;
}

public sealed class ThrowingPaymentHandler
{
    public void Handle(ChargePayment message) => throw new InvalidOperationException("payment failed");
}

public sealed class WaitsForCancellationHandler
{
    public Task Handle(ChargePayment message, CancellationToken cancellationToken) =>
        Task.Delay(Timeout.Infinite, cancellationToken);
}
