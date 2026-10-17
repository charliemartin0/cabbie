namespace Cabbie.Application.Bus;

/// <summary>
/// Optional schedule on Invoke/Publish. Delay and Until together is ambiguous.
/// </summary>
public sealed record DeliveryOptions
{
    public TimeSpan? Delay { get; init; }

    public DateTimeOffset? Until { get; init; }

    public bool HasSchedule => Delay is not null || Until is not null;
}
