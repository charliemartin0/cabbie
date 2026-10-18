namespace Cabbie.EntityFrameworkCore;

public sealed class EfOutboxRelayOptions
{
    /// <summary>How long the relay waits between passes.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Most rows one pass sends.</summary>
    public int BatchSize { get; init; } = 50;
}
