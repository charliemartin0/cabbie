namespace Cabbie.Application.Scheduling;

/// <summary>
/// Overlapping PlayDue on the same hold is not supported.
/// </summary>
public sealed class PlayDueInProgress : Exception
{
    public PlayDueInProgress()
        : base("PlayDue is already in progress on this hold.")
    {
    }
}
