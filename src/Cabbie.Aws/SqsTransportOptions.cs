namespace Cabbie.Aws;

/// <summary>Tuning for <see cref="SqsTransport"/>. Whole-second values are required where SQS takes seconds.</summary>
public sealed class SqsTransportOptions
{
    /// <summary>
    /// Sent with every receive. A message not acked within this time becomes visible again, so a
    /// handler that runs longer is redelivered (the inbox dedupes). Also the redelivery delay when a
    /// listener dies mid-flight.
    /// </summary>
    public TimeSpan VisibilityTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Long-poll wait, 0 to 20 whole seconds.</summary>
    public TimeSpan WaitTime { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Messages per receive, 1 to 10.</summary>
    public int MaxMessages { get; init; } = 10;

    /// <summary>How long a nacked message stays invisible before redelivery.</summary>
    public TimeSpan NackDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Pause after a failed receive call.</summary>
    public TimeSpan PollErrorDelay { get; init; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(VisibilityTimeout, TimeSpan.Zero);
        if (VisibilityTimeout > TimeSpan.FromHours(12) || !IsWholeSeconds(VisibilityTimeout))
        {
            throw new ArgumentOutOfRangeException(nameof(VisibilityTimeout), VisibilityTimeout, "Must be whole seconds, at most 12 hours.");
        }

        if (WaitTime < TimeSpan.Zero || WaitTime > TimeSpan.FromSeconds(20) || !IsWholeSeconds(WaitTime))
        {
            throw new ArgumentOutOfRangeException(nameof(WaitTime), WaitTime, "Must be whole seconds from 0 to 20.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxMessages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxMessages, 10);
        if (NackDelay < TimeSpan.Zero || NackDelay > TimeSpan.FromHours(12) || !IsWholeSeconds(NackDelay))
        {
            throw new ArgumentOutOfRangeException(nameof(NackDelay), NackDelay, "Must be whole seconds, at most 12 hours.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(PollErrorDelay, TimeSpan.Zero);
    }

    private static bool IsWholeSeconds(TimeSpan value) => value.Ticks % TimeSpan.TicksPerSecond == 0;
}
