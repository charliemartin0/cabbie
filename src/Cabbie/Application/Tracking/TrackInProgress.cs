namespace Cabbie.Application.Tracking;

/// <summary>
/// Overlapping tracked until-quiet on the same bus is not supported.
/// </summary>
public sealed class TrackInProgress : Exception
{
    public TrackInProgress()
        : base("A tracked session is already in progress on this bus.")
    {
    }
}
