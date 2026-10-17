using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Transports;

/// <summary>
/// Hands one inbound envelope to Execution and reports what the transport must do with it.
/// The outcome is returned only after the executor has finished, so a listener never
/// acknowledges an envelope whose handlers are still running. Delivery is at-least-once:
/// handlers must tolerate duplicates until the inbox (roadmap item 5).
/// </summary>
public interface IReceiver
{
    Task<ReceiveOutcome> ReceiveAsync(Envelope envelope, CancellationToken cancellationToken);
}
