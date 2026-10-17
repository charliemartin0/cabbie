using System.Collections.Concurrent;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Infrastructure.LocalQueues;

/// <summary>
/// Destination → local queue agent. One agent per destination URI, created lazily.
/// <see cref="Cabbie.Infrastructure.Transports.LocalTransport"/> sends and listens through
/// it. Agents created through <see cref="For"/> share the catalog's <see cref="IReceiver"/>,
/// so every queue runs the same Execution.
/// </summary>
public sealed class LocalQueueCatalog
{
    private readonly ConcurrentDictionary<string, LocalQueueAgent> _agents = new(StringComparer.Ordinal);
    private readonly IReceiver _receiver;

    public LocalQueueCatalog(IReceiver receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        _receiver = receiver;
    }

    public IEnumerable<LocalQueueAgent> All => _agents.Values;

    public LocalQueueAgent For(Destination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        string key = destination.Value.ToString();
        return _agents.GetOrAdd(key, _ => new LocalQueueAgent(key, _receiver));
    }

    /// <summary>
    /// The agent for <paramref name="destination"/> bound to <paramref name="receiver"/>.
    /// An agent already bound to a different receiver is a configuration error.
    /// </summary>
    internal LocalQueueAgent Listen(Destination destination, IReceiver receiver)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(receiver);
        string key = destination.Value.ToString();
        LocalQueueAgent agent = _agents.GetOrAdd(key, _ => new LocalQueueAgent(key, receiver));
        return ReferenceEquals(agent.Receiver, receiver)
            ? agent
            : throw new InvalidOperationException($"Local queue '{key}' is already bound to a different receiver.");
    }
}
