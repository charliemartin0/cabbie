namespace Cabbie.Domain.Envelope.ValueObjects;

/// <summary>
/// Envelope.Id. A Guid that identifies this envelope for retries, inbox dedupe, and tracking.
/// </summary>
public record EnvelopeId(Guid Value);
