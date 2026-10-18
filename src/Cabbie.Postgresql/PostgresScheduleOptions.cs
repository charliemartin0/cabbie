namespace Cabbie.Postgresql;

public sealed class PostgresScheduleOptions
{
    /// <summary>
    /// How long a claimed scheduled message stays invisible to other pollers. A poller that crashes
    /// mid-play loses its claim after this long and the message is delivered again.
    /// </summary>
    public TimeSpan ClaimLease { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The most scheduled messages one claim returns.</summary>
    public int BatchSize { get; init; } = 500;
}
