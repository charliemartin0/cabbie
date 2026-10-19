namespace Cabbie.Application.Scheduling;

/// <summary>
/// How <see cref="MessageScheduler"/> treats a scheduled row whose play fails outside the error policy:
/// the row backs off exponentially (so it cannot starve the rows behind it) and, once it has failed
/// <see cref="MaxAttempts"/> times, is moved to the error queue.
/// </summary>
public sealed class ScheduledRetryPolicy
{
    /// <summary>Failed plays after which the row is dead-lettered.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Wait after the first failed play; doubles per further failure.</summary>
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Upper bound on the wait between plays.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(InitialBackoff, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxBackoff, InitialBackoff);
    }

    /// <summary>The wait after the <paramref name="failures"/>-th failed play (1-based).</summary>
    public TimeSpan BackoffAfter(int failures)
    {
        double factor = Math.Pow(2, Math.Min(Math.Max(failures - 1, 0), 30));
        double ticks = Math.Min(InitialBackoff.Ticks * factor, MaxBackoff.Ticks);
        return TimeSpan.FromTicks((long)ticks);
    }
}
