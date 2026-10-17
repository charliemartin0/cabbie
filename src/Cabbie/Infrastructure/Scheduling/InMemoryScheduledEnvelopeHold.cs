using Cabbie.Application.Scheduling;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Infrastructure.Scheduling;

public sealed class InMemoryScheduledEnvelopeHold : IScheduledEnvelopeHold
{
    private readonly List<Envelope> _held = [];
    private bool _playing;

    public IReadOnlyList<Envelope> Peek() => [.. _held];

    public void Park(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _held.Add(envelope);
    }

    public bool TryRemove(EnvelopeId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        int index = _held.FindIndex(envelope => envelope.Id.Value == id.Value);
        if (index < 0)
        {
            return false;
        }

        _held.RemoveAt(index);
        return true;
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
