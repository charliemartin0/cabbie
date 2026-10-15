namespace Cabbie.Domain.Messaging.ValueObjects;

/// <summary>
/// The wire name is not in the catalog. A handled failure, not an exception.
/// </summary>
public record UnknownMessageType(MessageType Name) : MessageTypeLookup;
