namespace Cabbie.Application.Execution;

/// <summary>
/// Invoke applies retry/cooldown only. Scheduled (PlayDue) may park via ScheduleRetry.
/// </summary>
public enum InvocationKind
{
    Invoke,
    Scheduled
}
