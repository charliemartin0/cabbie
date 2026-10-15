using Cabbie.Application.Tracking;

namespace Cabbie.Application.Execution;

/// <summary>
/// Named wrap of a handler throw. Inner exception is what Handle threw.
/// Session is set on tracked waits; null when untracked.
/// </summary>
public sealed class HandlerFault : Exception
{
    public HandlerFault(Exception innerException)
        : base("The handler failed.", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }

    public TrackedSession? Session { get; set; }
}
