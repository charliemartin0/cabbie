using Cabbie.Application.Bus;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;

namespace Cabbie.Infrastructure.Transports;

/// <summary>
/// The local:// transport. <see cref="SendAsync"/> hands envelopes to
/// <see cref="IPublishEnqueuer"/> (LocalQueueCatalog by default) so outbound
/// envelopes join the same per-destination agent that Mediator.PublishAsync uses.
/// <see cref="DeliverAsync"/> runs inbound envelopes through the shared
/// <see cref="MessageDelivery"/> — there is no wire to read for local://, so the listen
/// path is synchronous on the caller's thread. Mirrors <c>LocalQueueAgent</c> for the
/// listen side but skips the channel/worker.
/// </summary>
public sealed class LocalTransport : ITransport
{
    private readonly IPublishEnqueuer _enqueuer;
    private readonly DispatchHandler _dispatch;

    public LocalTransport(IPublishEnqueuer enqueuer, MessageDelivery delivery)
        : this(enqueuer, (envelope, cancellationToken) =>
            delivery.Dispatch(envelope, scheduled: true, cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(enqueuer);
        ArgumentNullException.ThrowIfNull(delivery);
    }

    /// <summary>
    /// Dispatch hook for the host: inbound envelopes must route saga handlers through
    /// the saga orchestration (load/save), not the plain executor path.
    /// </summary>
    internal LocalTransport(IPublishEnqueuer enqueuer, DispatchHandler dispatch)
    {
        ArgumentNullException.ThrowIfNull(enqueuer);
        ArgumentNullException.ThrowIfNull(dispatch);
        _enqueuer = enqueuer;
        _dispatch = dispatch;
    }

    public string Scheme => "local";

    public ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _enqueuer.Enqueue(envelope);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DeliverAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        await _dispatch(envelope, cancellationToken);
    }
}