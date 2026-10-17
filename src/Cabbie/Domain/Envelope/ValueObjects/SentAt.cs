namespace Cabbie.Domain.Envelope.ValueObjects;

/// <summary>
/// Envelope.SentAt. When the envelope was created/sent. UTC DateTimeOffset.
/// </summary>
public record SentAt(DateTimeOffset Value);
