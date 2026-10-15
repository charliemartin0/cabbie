using Cabbie.Application.Discovery;
using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Middleware;

/// <summary>
/// Transactional-outbox enforcement around every handler attempt. Opens one short-lived
/// <see cref="IOutboxTransaction"/> per attempt, exposes it as the ambient
/// <see cref="OutboxTransactionScope.Current"/> during the handler, and registers it under
/// the handler envelope id so the owning call site (which runs after this middleware's
/// flow is gone) can take it, stage the cascades through
/// <see cref="Cabbie.Application.Bus.MessageDelivery.DispatchOutgoing"/>, and commit in a
/// <c>finally</c> AFTER the saga state was saved and the immediate-publish attempt.
/// <para>
/// This middleware only begins and rolls back: it never stages or commits. On the success
/// path the registration is left for the call site; on the throw path it rolls back,
/// unregisters, and restores the ambient before the exception escapes. A handler or saga
/// that explicitly completed the transaction itself is not double-touched — the second
/// completion throws <see cref="OutboxTransactionAlreadyCompletedException"/>, which is
/// swallowed here.
/// </para>
/// </summary>
public sealed class TransactionalOutboxMiddleware : IMessageMiddleware
{
    private readonly IOutboxStore _outbox;

    public TransactionalOutboxMiddleware(IOutboxStore outbox)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    public async Task<object?> InvokeAsync(
        Envelope envelope,
        DiscoveredHandler handler,
        Func<Task<object?>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        IOutboxTransaction tx = _outbox.BeginOutboxTransaction();
        OutboxTransactionScope.Begin(tx);
        OutboxTransactionScope.Register(envelope.Id, tx);

        try
        {
            return await next().ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OutboxTransactionAlreadyCompletedException)
            {
                // Handler already completed the transaction — nothing to roll back.
            }
            finally
            {
                // A failed attempt must never leave an orphaned registration for the
                // call site to commit, or leak the ambient scope into the next attempt —
                // even when RollbackAsync itself throws (the Postgres transaction will).
                OutboxTransactionScope.Unregister(envelope.Id);
                OutboxTransactionScope.End();
            }

            throw;
        }
    }
}