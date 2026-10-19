namespace Cabbie.Infrastructure.Scheduling;

public sealed class ScheduledMessagePollerOptions
{
    /// <summary>How long the poller waits between plays of due scheduled messages.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
}
