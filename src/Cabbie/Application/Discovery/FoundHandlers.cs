namespace Cabbie.Application.Discovery;

/// <summary>
/// One or more handler methods were discovered for the message type. Fan-out is allowed.
/// </summary>
public sealed record FoundHandlers(IReadOnlyList<DiscoveredHandler> Handlers) : HandlerLookup;
