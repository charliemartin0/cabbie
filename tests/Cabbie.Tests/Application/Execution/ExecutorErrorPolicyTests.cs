using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Execution;

/// <summary>
/// Prove-withs for A7: the Discard and Requeue Domain vocabulary is honored by the
/// Executor's retry loop instead of falling through to HandlerFault.
/// </summary>
public sealed class ExecutorErrorPolicyTests
{
    public ExecutorErrorPolicyTests()
    {
        TimeoutThrowingHandler.SeenAttempts.Clear();
    }

    [Fact]
    public async Task discard_returns_null_without_throwing()
    {
        Envelope envelope = ChargePaymentEnvelope();
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Discard();
        var executor = new Executor(policies);

        object? result = await executor.InvokeAsync(envelope, HandlerFor<TimeoutThrowingHandler>());

        Assert.Null(result);
        Assert.Equal([1], TimeoutThrowingHandler.SeenAttempts);
    }

    [Fact]
    public async Task requeue_re_enqueues_via_publish_enqueuer()
    {
        Envelope envelope = ChargePaymentEnvelope();
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Requeue();
        var enqueuer = new RecordingEnqueuer();
        var executor = new Executor(policies, enqueuer: () => enqueuer);

        object? result = await executor.InvokeAsync(envelope, HandlerFor<TimeoutThrowingHandler>());

        // The invocation completes without a fault; the envelope went back to the queue.
        Assert.Null(result);
        Envelope requeued = Assert.Single(enqueuer.Enqueued);
        Assert.Equal(envelope.Id, requeued.Id);
        Assert.Equal(envelope.Destination, requeued.Destination);

        // The handler was not re-invoked in-process.
        Assert.Equal([1], TimeoutThrowingHandler.SeenAttempts);
    }

    private static DiscoveredHandler HandlerFor<THandler>()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(THandler));
        return Assert.Single(catalog.Handlers);
    }

    private static Envelope ChargePaymentEnvelope() =>
        EnvelopeFactory.Create(
            message: new Message(new ChargePayment(1)),
            messageType: MessageTypeNaming.For(typeof(ChargePayment)));

    private sealed class RecordingEnqueuer : IPublishEnqueuer
    {
        public List<Envelope> Enqueued { get; } = [];

        public void Enqueue(Envelope envelope) => Enqueued.Add(envelope);
    }
}

/// <summary>
/// Own fixture (not the shared ExecutorTests one) so the parallel test classes
/// never race on static handler state.
/// </summary>
public sealed class TimeoutThrowingHandler
{
    public static List<int> SeenAttempts { get; } = [];

    public void Handle(ChargePayment message, Envelope envelope)
    {
        SeenAttempts.Add(envelope.Attempts.Value);
        throw new TimeoutException("always");
    }
}