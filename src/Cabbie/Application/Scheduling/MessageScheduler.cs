using Cabbie.Application.Bus;

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
        IReadOnlyList<ClaimedEnvelope> claimed = await _hold.ClaimDueAsync(asOf, cancellationToken);
        for (int i = 0; i < claimed.Count; i++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _dispatch(claimed[i].Envelope, cancellationToken);
                await _hold.CompleteAsync(claimed[i], CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                await ReleaseFrom(claimed, i);
                throw;
            }
            catch (Exception)
            {
                // The error policy already dead-lettered the envelope or parked its retry: it is consumed.
                await _hold.CompleteAsync(claimed[i], CancellationToken.None);
                await ReleaseFrom(claimed, i + 1);
                throw;
            }
        }
    }

    private async Task ReleaseFrom(IReadOnlyList<ClaimedEnvelope> claimed, int start)
    {
        for (int i = start; i < claimed.Count; i++)
        {
            await _hold.ReleaseAsync(claimed[i], CancellationToken.None);
        }
    }
}
