using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Application.Routing;

/// <summary>
/// Fluent destination for one published message type. Routing records the URI; it does not send.
/// </summary>
public sealed class PublishExpression
{
    private readonly RoutingCatalog _catalog;
    private readonly Type _messageType;

    public PublishExpression(RoutingCatalog catalog, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(messageType);
        _catalog = catalog;
        _messageType = messageType;
    }

    public void ToLocalQueue(string queueName)
    {
        ArgumentNullException.ThrowIfNull(queueName);
        To(new Uri($"local://{queueName}/"));
    }

    public void To(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _catalog.Register(_messageType, new Destination(destination));
    }
}
