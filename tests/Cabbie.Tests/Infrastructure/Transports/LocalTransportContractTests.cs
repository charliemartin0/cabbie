using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Infrastructure.LocalQueues;
using Cabbie.Infrastructure.Transports;
using Cabbie.Transports.Contracts;

namespace Cabbie.Tests.Infrastructure.Transports;

/// <summary>
/// <c>local://</c> through the serialising path (a <see cref="LocalTransport"/> with a codec).
/// Local drops a nacked envelope (the error policy already ran and the receiver logged it)
/// and binds one receiver per address, so it declares <see cref="NackBehaviour.Drop"/> and
/// <see cref="SharedAddressBehaviour.Rejected"/>.
/// </summary>
public sealed class LocalTransportContractTests : TransportContract
{
    private LocalTransport? _local;

    protected override TransportCapabilities Capabilities { get; } =
        new(NackBehaviour.Drop, SharedAddressBehaviour.Rejected);

    protected override TransportContractTimings Timings => TransportContractTimings.InProcess;

    private LocalTransport Local =>
        _local ?? throw new InvalidOperationException("InitializeAsync has not run.");

    protected override Task<ITransport> CreateTransportAsync(EnvelopeCodec codec)
    {
        _local = new LocalTransport(new LocalQueueCatalog(new UnboundReceiver()), codec);
        return Task.FromResult<ITransport>(_local);
    }

    protected override Task<Uri> CreateEndpointAsync() =>
        Task.FromResult(new Uri($"local://contract-{Guid.NewGuid():N}/"));

    protected override Task SendRawAsync(Uri endpoint, TransportMessage message)
    {
        Local.EnqueueEncoded(endpoint, message);
        return Task.CompletedTask;
    }

    protected override Task<IReadOnlyList<TransportMessage>> ReadDeadLettersAsync(Uri endpoint) =>
        Task.FromResult(Local.DeadLetters(endpoint));

    private sealed class UnboundReceiver : IReceiver
    {
        public Task<ReceiveOutcome> ReceiveAsync(Envelope envelope, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Contract endpoints are bound through CreateListener before anything is sent.");
    }
}
