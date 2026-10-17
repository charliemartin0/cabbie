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

    /// <summary>
    /// Route to an endpoint given as an absolute URI string, e.g. <c>local://orders</c>. The scheme
    /// picks the transport.
    /// </summary>
    public void To(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        To(new Uri(uri, UriKind.Absolute));
    }

    public void To(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.IsAbsoluteUri)
        {
            throw new ArgumentException("Endpoint URI must be absolute, e.g. local://orders.", nameof(destination));
        }

        _catalog.Register(_messageType, new Destination(destination));
    }
}
