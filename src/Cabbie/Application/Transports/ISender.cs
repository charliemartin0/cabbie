using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Transports;

/// <summary>
/// Outbound half of a transport endpoint: hand one envelope to the transport. Completing
/// means the transport accepted the envelope (enqueued it, wrote it to the broker), not
/// that a handler ran. Throws if the transport cannot accept it. A sender may own a client
/// or connection; <see cref="TransportRouter"/> caches one per endpoint and disposes it
/// when the host stops.
/// </summary>
public interface ISender : IAsyncDisposable
{
    ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default);
}
