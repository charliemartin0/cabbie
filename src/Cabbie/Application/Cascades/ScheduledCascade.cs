namespace Cabbie.Application.Cascades;

/// <summary>
/// A per-message absolute schedule: the wrapped message is parked in the scheduled
/// hold and fires at <see cref="Until"/>. Return it from a handler alongside immediate
/// cascades; DispatchOutgoing unwraps it before parking, so the wrapper itself is
/// never dispatched as a message body. A past Until parks silently — the next
/// PlayDue fires it immediately.
/// </summary>
public sealed record ScheduledCascade
{
    public object Message { get; init; }

    public DateTimeOffset Until { get; init; }

    public ScheduledCascade(object message, DateTimeOffset until)
    {
        ArgumentNullException.ThrowIfNull(message);
        Message = message;
        Until = until;
    }
}