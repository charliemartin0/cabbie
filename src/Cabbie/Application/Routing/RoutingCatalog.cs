using System.Reflection;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Application.Routing;

/// <summary>
/// Message type → destination URI. A table of rules, not a queue.
/// </summary>
public sealed class RoutingCatalog
{
    private readonly Dictionary<Type, Destination> _destinations = [];

    public PublishExpression PublishMessage<T>() => PublishMessage(typeof(T));

    public PublishExpression PublishMessage(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        return new PublishExpression(this, messageType);
    }

    public void Register(Type messageType, Destination destination)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(destination);
        _destinations[messageType] = destination;
    }

    public Destination For(object message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return For(message.GetType());
    }

    public Destination For(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        if (_destinations.TryGetValue(messageType, out Destination? destination))
        {
            return destination;
        }

        LocalQueueAttribute? localQueue = messageType.GetCustomAttribute<LocalQueueAttribute>(inherit: false);
        if (localQueue != null)
        {
            return new Destination(new Uri($"local://{localQueue.QueueName}/"));
        }

        return new Destination(new Uri($"local://{messageType.Name.ToLowerInvariant()}/"));
    }
}
