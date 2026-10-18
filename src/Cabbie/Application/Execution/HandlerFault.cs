using Cabbie.Application.Tracking;

namespace Cabbie.Application.Execution;

/// <summary>
/// Named wrap of a handler throw. Inner exception is what Handle threw.
/// Session is set on tracked waits; null when untracked. <see cref="DeadLettered"/> is true when
/// the error policy moved the envelope to the error queue before the fault was raised, so a
/// transport can acknowledge the envelope instead of redelivering it.
/// </summary>
public sealed class HandlerFault : Exception
{
    public HandlerFault(Exception innerException)
        : this(innerException, deadLettered: false)
    {
    }

    public HandlerFault(Exception innerException, bool deadLettered)
        : base("The handler failed.", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
        DeadLettered = deadLettered;
    }

    public bool DeadLettered { get; }

    public TrackedSession? Session { get; set; }
}
