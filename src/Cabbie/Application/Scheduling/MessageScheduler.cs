using System.Runtime.ExceptionServices;
using Cabbie.Application.Bus;
using Cabbie.Application.Execution;

namespace Cabbie.Application.Scheduling;

public interface IMessageScheduler
{
    Task PlayDue(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}

public sealed class MessageScheduler : IMessageScheduler
{
    private readonly IScheduledEnvelopeHold _hold;
    private readonly DispatchHandler _dispatch;
    private readonly IErrorQueue? _errorQueue;
    private readonly ScheduledRetryPolicy _retry;

    public MessageScheduler(
        MessageDelivery delivery,
        IScheduledEnvelopeHold hold,
        IErrorQueue? errorQueue = null,
        ScheduledRetryPolicy? retry = null)
        : this(hold, (envelope, cancellationToken) =>
            delivery.Dispatch(envelope, scheduled: true, cancellationToken), errorQueue, retry)
    {
        ArgumentNullException.ThrowIfNull(delivery);
    }

    /// <summary>
    /// Dispatch hook for the Mediator: scheduled envelopes must route saga handlers
    /// through the saga orchestration (load/save), not the plain executor path that
    /// would invoke them on a fresh instance and drop the state.
    /// </summary>
    internal MessageScheduler(
        IScheduledEnvelopeHold hold,
        DispatchHandler dispatch,
        IErrorQueue? errorQueue = null,
        ScheduledRetryPolicy? retry = null)
    {
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(dispatch);
        _hold = hold;
        _dispatch = dispatch;
        _errorQueue = errorQueue;
        _retry = retry ?? new ScheduledRetryPolicy();
        _retry.Validate();
    }

    /// <summary>
    /// Plays every claimed row. A failing row never blocks the rows behind it: it is deferred with
    /// backoff (or dead-lettered after <see cref="ScheduledRetryPolicy.MaxAttempts"/>) and the loop
    /// continues; the first failure is rethrown once the batch is done.
    /// </summary>
    public async Task PlayDue(DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        using IDisposable play = _hold.BeginPlay();
        IReadOnlyList<ClaimedEnvelope> claimed = await _hold.ClaimDueAsync(asOf, cancellationToken);
        Exception? firstFailure = null;
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
            catch (HandlerFault fault)
            {
                // The executor ran the error policy (dead-lettered or parked a retry): the envelope is consumed.
                await _hold.CompleteAsync(claimed[i], CancellationToken.None);
                firstFailure ??= fault;
            }
            catch (Exception exception)
            {
                // Failure outside the error policy (inbox, cascade send): back off this row only.
                await BackOff(claimed[i], exception, asOf);
                firstFailure ??= exception;
            }
        }

        if (firstFailure is not null)
        {
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }

    private async Task BackOff(ClaimedEnvelope claim, Exception cause, DateTimeOffset asOf)
    {
        int failures = claim.Failures + 1;
        if (_errorQueue is not null && failures >= _retry.MaxAttempts)
        {
            try
            {
                await _errorQueue.MoveAsync(claim.Envelope, cause, CancellationToken.None);
                await _hold.CompleteAsync(claim, CancellationToken.None);
                return;
            }
            catch (Exception)
            {
                // Dead-letter write failed: keep the row and try again after the backoff.
            }
        }

        await _hold.DeferAsync(claim, asOf + _retry.BackoffAfter(failures), CancellationToken.None);
    }

    private async Task ReleaseFrom(IReadOnlyList<ClaimedEnvelope> claimed, int start)
    {
        for (int i = start; i < claimed.Count; i++)
        {
            await _hold.ReleaseAsync(claimed[i], CancellationToken.None);
        }
    }
}
