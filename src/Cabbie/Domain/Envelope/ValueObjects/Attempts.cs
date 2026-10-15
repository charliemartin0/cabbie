namespace Cabbie.Domain.Envelope.ValueObjects;

/// <summary>
/// Envelope.Attempts. How many times this same envelope has been executed. First try is 1.
/// </summary>
public record Attempts(int Value);
