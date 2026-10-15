using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Infrastructure.Transports;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Transports;

/// <summary>
/// LocalTransport-specific behaviour: scheme is "local", SendAsync hands to
/// IPublishEnqueuer, DeliverAsync looks up the handler and runs it through
/// Execution (mirroring LocalQueueAgent for the inbound listen path).
/// </summary>
public sealed class LocalTransportTests
{
    public LocalTransportTests()
    {
        RecordingPlaceOrderHandler.Invocations.Clear();
        RecordingMissingHandler.SeenMessageTypes.Clear();
    }

    [Fact]
    public void local_transport_scheme_is_local()
    {
        ITransport transport = new LocalTransport(
            new RecordingEnqueuer(),
            new MessageDelivery(new HandlerCatalog(), new Executor(new ErrorPolicyCatalog())));
        Assert.Equal("local", transport.Scheme);
    }

    [Fact]
    public async Task local_transport_send_async_hands_envelope_to_publish_enqueuer()
    {
        var enqueuer = new RecordingEnqueuer();
        ITransport transport = new LocalTransport(
            enqueuer,
            new MessageDelivery(new HandlerCatalog(), new Executor(new ErrorPolicyCatalog())));
        var envelope = EnvelopeFactory.Create(
            destination: new Destination(new Uri("local://payments/")));

        await transport.SendAsync(envelope);

        Assert.Same(envelope, Assert.Single(enqueuer.Enqueued));
    }

    [Fact]
    public async Task local_transport_deliver_async_looks_up_handler_and_invokes_via_executor()
    {
        var handlers = new HandlerCatalog();
        handlers.Scan(typeof(RecordingPlaceOrderHandler));
        ITransport transport = new LocalTransport(
            new RecordingEnqueuer(),
            new MessageDelivery(handlers, new Executor(new ErrorPolicyCatalog())));

        var envelope = EnvelopeFactory.Create(
            message: new Message(new PlaceOrder(7)),
            destination: new Destination(new Uri("local://payments/")));

        await transport.DeliverAsync(envelope);

        RecordingPlaceOrderHandler.Invocation invocation = Assert.Single(RecordingPlaceOrderHandler.Invocations);
        Assert.Equal(7, invocation.OrderId);
        // Executor does not mutate Attempts on success; the envelope arrives at the
        // handler with its original attempts value (1, the EnvelopeFactory default).
        Assert.Equal(1, invocation.Envelope.Attempts.Value);
    }

    [Fact]
    public async Task local_transport_deliver_async_with_no_handler_routes_to_missing_handler()
    {
        var enqueuer = new RecordingEnqueuer();
        var missing = new RecordingMissingHandler();
        ITransport transport = new LocalTransport(
            enqueuer,
            new MessageDelivery(
                new HandlerCatalog(),
                new Executor(new ErrorPolicyCatalog(), missingHandler: missing)));

        var envelope = EnvelopeFactory.Create(
            message: new Message(new PlaceOrder(7)),
            destination: new Destination(new Uri("local://payments/")));

        await transport.DeliverAsync(envelope);

        Type seen = Assert.Single(RecordingMissingHandler.SeenMessageTypes);
        Assert.Equal(typeof(PlaceOrder), seen);
        Assert.Empty(enqueuer.Enqueued);
    }

    private sealed class RecordingEnqueuer : IPublishEnqueuer
    {
        public List<Envelope> Enqueued { get; } = new();

        public void Enqueue(Envelope envelope)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            Enqueued.Add(envelope);
        }
    }

    private sealed class RecordingMissingHandler : IMissingHandler
    {
        public static List<Type> SeenMessageTypes { get; } = new();

        public Task HandleAsync(Envelope envelope, CancellationToken cancellationToken = default)
        {
            SeenMessageTypes.Add(envelope.Message.Value.GetType());
            return Task.CompletedTask;
        }
    }

    public sealed class RecordingPlaceOrderHandler
    {
        public static List<Invocation> Invocations { get; } = new();

        public sealed record Invocation(int OrderId, Envelope Envelope);

        public void Handle(PlaceOrder message, Envelope envelope)
        {
            Invocations.Add(new Invocation(message.OrderId, envelope));
        }
    }
}
