using Cabbie.Application.Scheduling;
using Cabbie.Domain.Envelope;

namespace Cabbie.Infrastructure.Scheduling;

public sealed class InMemoryScheduledEnvelopeHold : IScheduledEnvelopeHold
{
    private readonly List<ClaimedEnvelope> _held = [];
    private readonly Dictionary<long, DateTimeOffset> _notBefore = [];
    private readonly Dictionary<long, ClaimedEnvelope> _claimed = [];
    private long _nextToken;
    private bool _playing;

    /// <summary>Envelopes parked and not currently claimed, in park order.</summary>
    public IReadOnlyList<Envelope> Peek() => [.. _held.Select(held => held.Envelope)];

    public ValueTask ParkAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _held.Add(new ClaimedEnvelope(envelope, ++_nextToken));
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ClaimedEnvelope>> ClaimDueAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        ClaimedEnvelope[] due =
        [
            .. _held
                .Where(held => EffectiveTime(held) is { } at && at <= asOf)
                .OrderBy(EffectiveTime)
                .ThenBy(held => held.Token)
        ];
        foreach (ClaimedEnvelope claim in due)
        {
            _held.Remove(claim);
            _claimed[claim.Token] = claim;
        }

        return ValueTask.FromResult<IReadOnlyList<ClaimedEnvelope>>(due);
    }

    public ValueTask CompleteAsync(ClaimedEnvelope claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        _claimed.Remove(claim.Token);
        _notBefore.Remove(claim.Token);
        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAsync(ClaimedEnvelope claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (_claimed.Remove(claim.Token))
        {
            _held.Add(claim);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DeferAsync(
        ClaimedEnvelope claim,
        DateTimeOffset notBefore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (_claimed.Remove(claim.Token))
        {
            _held.Add(claim with { Failures = claim.Failures + 1 });
            _notBefore[claim.Token] = notBefore;
        }

        return ValueTask.CompletedTask;
    }

    private DateTimeOffset? EffectiveTime(ClaimedEnvelope held)
    {
        DateTimeOffset? due = held.Envelope.DeliverBy.Value;
        return due is { } at && _notBefore.TryGetValue(held.Token, out DateTimeOffset later) && later > at ? later : due;
    }

    public IDisposable BeginPlay()
    {
        if (_playing)
        {
            throw new PlayDueInProgress();
        }

        _playing = true;
        return new PlaySession(this);
    }

    private sealed class PlaySession : IDisposable
    {
        private readonly InMemoryScheduledEnvelopeHold _hold;

        public PlaySession(InMemoryScheduledEnvelopeHold hold) => _hold = hold;

        public void Dispose() => _hold._playing = false;
    }
}
