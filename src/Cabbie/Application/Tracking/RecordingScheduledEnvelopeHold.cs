using Cabbie.Application.Scheduling;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Tracking;

internal sealed class RecordingScheduledEnvelopeHold : IScheduledEnvelopeHold
{
    private readonly IScheduledEnvelopeHold _inner;
    private readonly Action<Envelope> _onPark;

    public RecordingScheduledEnvelopeHold(IScheduledEnvelopeHold inner, Action<Envelope> onPark)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(onPark);
        _inner = inner;
        _onPark = onPark;
    }

    public ValueTask ParkAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        _onPark(envelope);
        return _inner.ParkAsync(envelope, cancellationToken);
    }

    public ValueTask<IReadOnlyList<ClaimedEnvelope>> ClaimDueAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default) => _inner.ClaimDueAsync(asOf, cancellationToken);

    public ValueTask CompleteAsync(ClaimedEnvelope claim, CancellationToken cancellationToken = default) =>
        _inner.CompleteAsync(claim, cancellationToken);

    public ValueTask ReleaseAsync(ClaimedEnvelope claim, CancellationToken cancellationToken = default) =>
        _inner.ReleaseAsync(claim, cancellationToken);

    public ValueTask DeferAsync(
        ClaimedEnvelope claim,
        DateTimeOffset notBefore,
        CancellationToken cancellationToken = default) => _inner.DeferAsync(claim, notBefore, cancellationToken);

    public IDisposable BeginPlay() => _inner.BeginPlay();
}
