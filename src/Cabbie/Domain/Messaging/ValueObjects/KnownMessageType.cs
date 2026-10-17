namespace Cabbie.Domain.Messaging.ValueObjects;

/// <summary>
/// The wire name maps to a registered CLR message type.
/// </summary>
public record KnownMessageType(Type ClrType) : MessageTypeLookup;
