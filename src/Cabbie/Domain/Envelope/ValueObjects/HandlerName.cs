namespace Cabbie.Domain.Envelope.ValueObjects;

/// <summary>
/// Envelope.Handler. Names the one handler this envelope is addressed to; null on an envelope
/// that fans out to every handler of its message type.
/// </summary>
public record HandlerName(string Value);
