using System.Collections.Concurrent;
using Cabbie.Application.Bus;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Infrastructure.LocalQueues;

/// <summary>
/// Destination → local queue agent. One agent per destination URI, created lazily.
/// Also the <see cref="IPublishEnqueuer"/> the Mediator publishes through. Agents share
/// the single <see cref="MessageDelivery"/> so every queue runs the same Executor.
/// </summary>
public sealed class LocalQueueCatalog : IPublishEnqueuer
{
    private readonly ConcurrentDictionary<string, LocalQueueAgent> _agents = new(StringComparer.Ordinal);
    private readonly DispatchHandler _dispatch;

    public LocalQueueCatalog(MessageDelivery delivery)
        : this((envelope, cancellationToken) =>
            delivery.Dispatch(envelope, scheduled: true, cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(delivery);
    }

    /// <summary>
    /// Dispatch hook for the host: agents route saga handlers through the saga
    /// orchestration (load/save), not the plain executor path.
    /// </summary>
    internal LocalQueueCatalog(DispatchHandler dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        _dispatch = dispatch;
    }

    public IEnumerable<LocalQueueAgent> All => _agents.Values;

    public LocalQueueAgent For(Destination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        string key = destination.Value.ToString();
        return _agents.GetOrAdd(key, _ => new LocalQueueAgent(key, _dispatch));
    }

    public void Enqueue(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        For(envelope.Destination).Enqueue(envelope);
    }
}