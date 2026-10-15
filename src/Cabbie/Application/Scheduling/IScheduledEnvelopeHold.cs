using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Application.Scheduling;

/// <summary>
/// Parking lot for delayed envelopes. Peek does not play. Persistence replaces the in-memory impl later.
/// </summary>
public interface IScheduledEnvelopeHold
{
    IReadOnlyList<Envelope> Peek();

    void Park(Envelope envelope);

    bool TryRemove(EnvelopeId id);

    IDisposable BeginPlay();
}
