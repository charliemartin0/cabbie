using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Transports;

/// <summary>
/// Outbound half of a transport endpoint: hand one envelope to the transport. Completing
/// means the transport accepted the envelope (enqueued it, wrote it to the broker), not
/// that a handler ran. Throws if the transport cannot accept it.
/// </summary>
public interface ISender
{
    ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default);
}
