namespace Cabbie.Domain.Errors.ValueObjects;

/// <summary>
/// Park the same envelope and deliver it later. Delay must be greater than zero.
/// </summary>
public sealed record ScheduleRetry(TimeSpan Delay) : ErrorAction;
