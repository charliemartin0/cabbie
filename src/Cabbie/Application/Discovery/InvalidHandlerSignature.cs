namespace Cabbie.Application.Discovery;

/// <summary>
/// A scanned handler type is not a valid handler. Scan throws; it does not silently omit the method.
/// </summary>
public sealed class InvalidHandlerSignature : Exception
{
    public Type HandlerType { get; }

    public InvalidHandlerSignature(Type handlerType, string message)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        HandlerType = handlerType;
    }
}
