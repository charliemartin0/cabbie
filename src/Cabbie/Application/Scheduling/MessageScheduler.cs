using Cabbie.Application.Bus;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Scheduling;

public interface IMessageScheduler
{
    Task PlayDue(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}

public sealed class MessageScheduler : IMessageScheduler
{
    private readonly IScheduledEnvelopeHold _hold;
    private readonly DispatchHandler _dispatch;

    public MessageScheduler(MessageDelivery delivery, IScheduledEnvelopeHold hold)
        : this(hold, (envelope, cancellationToken) =>
            delivery.Dispatch(envelope, scheduled: true, cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(delivery);
    }

    /// <summary>
    /// Dispatch hook for the Mediator: scheduled envelopes must route saga handlers
    /// through the saga orchestration (load/save), not the plain executor path that
    /// would invoke them on a fresh instance and drop the state.
    /// </summary>
    internal MessageScheduler(IScheduledEnvelopeHold hold, DispatchHandler dispatch)
    {
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(dispatch);
        _hold = hold;
        _dispatch = dispatch;
    }

    public async Task PlayDue(DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        using IDisposable play = _hold.BeginPlay();
        IReadOnlyList<Envelope> snapshot = [.. Due(asOf)];
        foreach (Envelope envelope in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_hold.TryRemove(envelope.Id))
            {
                continue;
            }

            try
            {
                await InvokeDue(envelope, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _hold.Park(envelope);
                throw;
            }
        }
    }

    private IEnumerable<Envelope> Due(DateTimeOffset asOf) =>
        _hold.Peek()
            .Where(envelope => envelope.DeliverBy.Value is { } due && due <= asOf)
            .OrderBy(envelope => envelope.DeliverBy.Value)
            .ThenBy(envelope => IndexOf(envelope));

    private int IndexOf(Envelope envelope)
    {
        IReadOnlyList<Envelope> held = _hold.Peek();
        for (int i = 0; i < held.Count; i++)
        {
            if (held[i].Id.Value == envelope.Id.Value)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private Task InvokeDue(Envelope envelope, CancellationToken cancellationToken) =>
        _dispatch(envelope, cancellationToken);
}
