using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Transports.Contracts;

/// <summary>
/// The receiver every contract test listens with. It records each delivery itself and the
/// suite asserts only on that record, never on a side effect that happens after the signal.
/// </summary>
/// <param name="decide">
/// Picks the outcome from the envelope and its 1-based delivery count for that envelope id.
/// The default always acks.
/// </param>
internal sealed class ContractReceiver(Func<Envelope, int, ReceiveOutcome>? decide = null) : IReceiver
{
    private readonly Lock _lock = new();
    private readonly List<Envelope> _received = [];
    private readonly Dictionary<EnvelopeId, int> _counts = [];

    /// <summary>When set, every delivery waits here after it is recorded.</summary>
    public TaskCompletionSource? Gate { get; init; }

    /// <summary>Completes on the first delivery.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<Envelope> Received
    {
        get
        {
            lock (_lock)
            {
                return [.. _received];
            }
        }
    }

    public int DeliveriesOf(EnvelopeId id)
    {
        lock (_lock)
        {
            return _counts.GetValueOrDefault(id);
        }
    }

    public async Task<ReceiveOutcome> ReceiveAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        int count;
        lock (_lock)
        {
            _received.Add(envelope);
            count = _counts.GetValueOrDefault(envelope.Id) + 1;
            _counts[envelope.Id] = count;
        }

        Entered.TrySetResult();
        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

        return decide is null ? ReceiveOutcome.Ack : decide(envelope, count);
    }
}
