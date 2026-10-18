namespace Cabbie.Domain.Errors.ValueObjects;

/// <summary>
/// Re-send the envelope as the next attempt for the failed handler only, after Delay.
/// </summary>
public sealed record Requeue(TimeSpan Delay) : ErrorAction;
