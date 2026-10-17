namespace Cabbie.Domain.Envelope.ValueObjects;

/// <summary>
/// Envelope.CausationId. Id of the envelope whose handler produced this one; null for messages sent from outside a handler.
/// </summary>
public record CausationId(Guid Value);
