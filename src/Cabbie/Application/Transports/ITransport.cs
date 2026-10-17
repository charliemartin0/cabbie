using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Transports;

/// <summary>
/// A transport carries Envelopes between processes. The local:// transport hands
/// outbound envelopes to the in-process queue and routes inbound envelopes straight
/// through Execution. A future tcp:// transport (Infrastructure/Transports) reads and
/// writes bytes on a socket; Cabbie.RabbitMQ is its ITransport; Cabbie.Http is
/// another front door into the same Execution. Application knows only ITransport —
/// the concrete type is wired in Hosting.
/// </summary>
public interface ITransport
{
    /// <summary>
    /// URI scheme this transport handles, e.g. <c>local</c>, <c>tcp</c>, <c>rabbitmq</c>.
    /// Routing uses the scheme on <see cref="Cabbie.Domain.Envelope.ValueObjects.Destination"/>
    /// to pick the owning transport.
    /// </summary>
    string Scheme { get; }

    /// <summary>
    /// Outbound: hand the envelope to this transport's send path. <c>local://</c>
    /// enqueues to the in-process queue (fire-and-forget); <c>tcp://</c> serializes
    /// and writes to the wire.
    /// </summary>
    ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inbound: deliver a deserialized envelope to Execution. Wire transports
    /// deserialize bytes to <see cref="Envelope"/> before calling this; the caller's
    /// <c>local://</c> variant looks up the handler and invokes via <c>Executor</c>
    /// directly — no queue, no worker, the caller is already off the bus.
    /// </summary>
    ValueTask DeliverAsync(Envelope envelope, CancellationToken cancellationToken = default);
}
