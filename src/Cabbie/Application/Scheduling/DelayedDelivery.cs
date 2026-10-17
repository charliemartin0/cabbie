using System.Reflection;
using Cabbie.Application.Bus;
using Cabbie.Domain.Sagas;

namespace Cabbie.Application.Scheduling;

/// <summary>
/// When a message should run. Null means the immediate path (no hold).
/// </summary>
public static class DelayedDelivery
{
    public static DateTimeOffset? DueAt(Type messageType, DeliveryOptions? options, DateTimeOffset sentAt)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        DeliveryOptions? schedule = options;
        if (schedule is { Delay: not null, Until: not null })
        {
            throw new AmbiguousDeliveryOptions();
        }

        if (schedule?.Delay is { } delay)
        {
            if (delay == TimeSpan.Zero)
            {
                return null;
            }

            return sentAt + delay;
        }

        if (schedule?.Until is { } until)
        {
            if (until == sentAt)
            {
                return null;
            }

            return until;
        }

        var timeout = messageType.GetCustomAttribute<TimeoutAttribute>();
        if (timeout is not null && timeout.Delay > TimeSpan.Zero)
        {
            return sentAt + timeout.Delay;
        }

        return null;
    }
}
