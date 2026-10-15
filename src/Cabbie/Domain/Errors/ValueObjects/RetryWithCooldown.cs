namespace Cabbie.Domain.Errors.ValueObjects;

/// <summary>
/// Retry after a delay.
/// </summary>
public sealed record RetryWithCooldown(TimeSpan Delay) : ErrorAction;
