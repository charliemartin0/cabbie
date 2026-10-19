namespace Cabbie.EntityFrameworkCore;

public sealed class EfOutboxRelayOptions
{
    /// <summary>How long the relay waits between passes.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Most rows one pass sends.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>Total send attempts before a failing row is dead-lettered.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Delay after the first failed send; doubles with each further failure.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);
}
