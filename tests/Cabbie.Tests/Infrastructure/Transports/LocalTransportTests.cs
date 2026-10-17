using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Infrastructure.LocalQueues;
using Cabbie.Infrastructure.Serialization;
using Cabbie.Infrastructure.Transports;
using Cabbie.Tests.Application.Transports;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Infrastructure.Transports;

public sealed class LocalTransportTests
{
    private static readonly Uri Orders = new("local://orders");

    [Fact]
    public async Task local_round_trip_through_the_serializer_preserves_headers_and_body()
    {
        var receiver = new RecordingReceiver();
        var transport = new LocalTransport(new LocalQueueCatalog(receiver), Codec());
        IListener listener = transport.CreateListener(Orders, receiver);
        await listener.StartAsync(CancellationToken.None);
        Envelope sent = EnvelopeFactory.Create(
            message: new Message(new PlaceOrder(7)),
            destination: new Destination(Orders),
            headers: new Headers(new Dictionary<string, string> { ["trace-id"] = "abc-123" }),
            causationId: new CausationId(Guid.NewGuid()));

        await transport.CreateSender(Orders).SendAsync(sent);
        await receiver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await listener.StopAsync(CancellationToken.None);

        Envelope received = Assert.Single(receiver.Received);
        Assert.NotSame(sent, received);
        Assert.Equal(sent.Id, received.Id);
        Assert.Equal(sent.Headers, received.Headers);
        Assert.Equal(sent.CausationId, received.CausationId);
        Assert.Equal(new PlaceOrder(7), received.Message.Value);
        Assert.NotEqual(0, received.Data.Value.Length);
    }

    [Fact]
    public async Task local_listener_stop_waits_for_the_in_flight_envelope_and_then_takes_no_more()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new RecordingReceiver { Gate = gate };
        var transport = new LocalTransport(new LocalQueueCatalog(receiver));
        IListener listener = transport.CreateListener(Orders, receiver);
        await listener.StartAsync(CancellationToken.None);
        ISender sender = transport.CreateSender(Orders);
        Envelope envelope = EnvelopeFactory.Create(destination: new Destination(Orders));

        await sender.SendAsync(envelope);
        await receiver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task stopping = listener.StopAsync(CancellationToken.None);
        await Task.Delay(100);
        Assert.False(stopping.IsCompleted);

        gate.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        await sender.SendAsync(EnvelopeFactory.Create(destination: new Destination(Orders)));
        await Task.Delay(100);

        Assert.Equal(envelope.Id, Assert.Single(receiver.Received).Id);
    }

    [Fact]
    public async Task deferred_envelope_goes_back_on_the_queue_and_is_received_again()
    {
        var receiver = new DefersOnceReceiver();
        var transport = new LocalTransport(new LocalQueueCatalog(receiver));
        IListener listener = transport.CreateListener(Orders, receiver);
        await listener.StartAsync(CancellationToken.None);

        await transport.CreateSender(Orders).SendAsync(EnvelopeFactory.Create(destination: new Destination(Orders)));
        await receiver.Twice.WaitAsync(TimeSpan.FromSeconds(5));
        await listener.StopAsync(CancellationToken.None);

        Assert.Equal(2, receiver.Count);
    }

    [Fact]
    public void listening_on_one_local_address_with_two_receivers_throws()
    {
        var first = new RecordingReceiver();
        var transport = new LocalTransport(new LocalQueueCatalog(first));
        transport.CreateListener(Orders, first);

        Assert.Throws<InvalidOperationException>(() => transport.CreateListener(Orders, new RecordingReceiver()));
    }

    private static EnvelopeCodec Codec()
    {
        var messageTypes = new MessageTypeCatalog();
        messageTypes.Register(typeof(PlaceOrder));
        return new EnvelopeCodec(new JsonMessageSerializer(), messageTypes);
    }

    private sealed class DefersOnceReceiver : IReceiver
    {
        private readonly TaskCompletionSource _twice = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task Twice => _twice.Task;

        public Task<ReceiveOutcome> ReceiveAsync(Envelope envelope, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _count) == 1)
            {
                return Task.FromResult(ReceiveOutcome.Defer);
            }

            _twice.TrySetResult();
            return Task.FromResult(ReceiveOutcome.Ack);
        }
    }
}
