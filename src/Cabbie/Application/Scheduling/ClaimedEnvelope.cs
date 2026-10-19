using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Scheduling;

/// <summary>
/// A due envelope leased from an <see cref="IScheduledEnvelopeHold"/>. The token identifies the
/// claim to <see cref="IScheduledEnvelopeHold.CompleteAsync"/>, <see cref="IScheduledEnvelopeHold.ReleaseAsync"/>
/// and <see cref="IScheduledEnvelopeHold.DeferAsync"/>. <paramref name="Failures"/> counts earlier failed plays.
/// </summary>
public sealed record ClaimedEnvelope(Envelope Envelope, long Token, int Failures = 0);
