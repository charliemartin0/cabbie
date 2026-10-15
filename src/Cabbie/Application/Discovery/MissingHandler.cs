namespace Cabbie.Application.Discovery;

/// <summary>
/// No handler method was discovered for this CLR message type. A handled result, not an exception.
/// </summary>
public sealed record MissingHandler(Type MessageType) : HandlerLookup;
