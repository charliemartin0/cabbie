namespace Cabbie.Application.Execution;

/// <summary>
/// ScheduleRetry is Publish/PlayDue only. Invoke uses Retry or RetryWithCooldown.
/// </summary>
public sealed class ScheduleRetryNotSupportedOnInvoke : Exception
{
    public ScheduleRetryNotSupportedOnInvoke()
        : base("ScheduleRetry is not supported on Invoke. Use Retry or RetryWithCooldown.")
    {
    }
}
