using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Infrastructure.LocalQueues;
using Cabbie.Infrastructure.Transports;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Infrastructure.Transports;

public sealed class LocalTransportTests
{
    private static readonly Uri Orders = new("local://orders");

    [Fact]
    public async Task deferred_envelope_passed_by_reference_goes_back_on_the_queue_and_is_received_again()
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
