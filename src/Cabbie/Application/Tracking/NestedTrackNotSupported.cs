namespace Cabbie.Application.Tracking;

/// <summary>
/// Handlers return messages. They do not start tracked sessions.
/// </summary>
public sealed class NestedTrackNotSupported : Exception
{
    public NestedTrackNotSupported()
        : base("A handler cannot start a tracked session.")
    {
    }
}
