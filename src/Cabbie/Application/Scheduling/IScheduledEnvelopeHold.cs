using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Scheduling;

/// <summary>
/// Parking lot for delayed envelopes. Due envelopes are claimed, played, then completed (forgotten)
/// or released (claimable again), so a durable hold can lease them across processes.
/// </summary>
public interface IScheduledEnvelopeHold
{
    ValueTask ParkAsync(Envelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claim envelopes whose delivery time is at or before <paramref name="asOf"/>, ordered by delivery
    /// time then park order. Claimed envelopes are invisible to other claimers until completed or released.
    /// </summary>
    ValueTask<IReadOnlyList<ClaimedEnvelope>> ClaimDueAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    /// <summary>The claimed envelope was played; forget it.</summary>
    ValueTask CompleteAsync(ClaimedEnvelope claim, CancellationToken cancellationToken = default);

    /// <summary>The claimed envelope was not played; make it claimable again.</summary>
    ValueTask ReleaseAsync(ClaimedEnvelope claim, CancellationToken cancellationToken = default);

    IDisposable BeginPlay();
}
