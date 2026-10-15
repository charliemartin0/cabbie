namespace Cabbie.Application.Discovery;

/// <summary>
/// Invoke has no discovered handler for this CLR message type. Catalog Lookup is MissingHandler; this is the throw.
/// </summary>
public sealed class HandlerNotFound : Exception
{
    public Type MessageType { get; }

    public HandlerNotFound(Type messageType)
        : base($"No handler was discovered for message type '{messageType}'.")
    {
        ArgumentNullException.ThrowIfNull(messageType);
        MessageType = messageType;
    }
}
