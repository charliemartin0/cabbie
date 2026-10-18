using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Scheduling;

/// <summary>
/// A due envelope leased from an <see cref="IScheduledEnvelopeHold"/>. The token identifies the
/// claim to <see cref="IScheduledEnvelopeHold.CompleteAsync"/> and <see cref="IScheduledEnvelopeHold.ReleaseAsync"/>.
/// </summary>
public sealed record ClaimedEnvelope(Envelope Envelope, long Token);
