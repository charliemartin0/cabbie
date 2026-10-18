using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Infrastructure.LocalQueues;

namespace Cabbie.Infrastructure.Transports;

/// <summary>
/// The <c>local://</c> transport: in-process queues behind the transport ports. A sender
/// enqueues to the per-address <see cref="LocalQueueAgent"/>; a listener starts that agent
/// bound to the given receiver and stops by draining it. Without an
/// <see cref="EnvelopeCodec"/> envelopes pass by reference (no serialization, as in-process
/// delivery always did); with one, every send round-trips through the serializer, which is
/// how the transport contract is exercised against a serializing transport.
/// </summary>
public sealed class LocalTransport : ITransport
{
    private readonly LocalQueueCatalog _queues;
    private readonly EnvelopeCodec? _codec;

    public LocalTransport(LocalQueueCatalog queues, EnvelopeCodec? codec = null)
    {
        ArgumentNullException.ThrowIfNull(queues);
        _queues = queues;
        _codec = codec;
    }

    public string Scheme => "local";

    public ISender CreateSender(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return new LocalSender(_queues.For(new Destination(address)), _codec);
    }

    public IListener CreateListener(Uri address, IReceiver receiver)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(receiver);
        return new LocalListener(address, _queues.Listen(new Destination(address), receiver));
    }

    private sealed class LocalSender(LocalQueueAgent agent, EnvelopeCodec? codec) : ISender
    {
        public ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            agent.Enqueue(codec is null ? envelope : codec.Decode(codec.Encode(envelope)));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class LocalListener(Uri address, LocalQueueAgent agent) : IListener
    {
        public Uri Address => address;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            agent.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => agent.DrainAsync(cancellationToken);
    }
}
