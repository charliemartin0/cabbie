using System.Collections.Concurrent;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Application.Persistence;

/// <summary>
/// Ambient holder for the in-flight <see cref="IOutboxTransaction"/>. The
/// TransactionalOutboxMiddleware opens one transaction per handler attempt and exposes it
/// through two channels:
/// <list type="bullet">
/// <item><description><see cref="Current"/> — an AsyncLocal ambient, visible during the
/// middleware's own flow (the handler attempt) and re-established by the owning call site
/// so <c>MessageDelivery.DispatchOutgoing</c> stages onto it.</description></item>
/// <item><description>the registration dictionary keyed by envelope id — AsyncLocal values
/// written by a callee async method do not flow back to the caller, so the middleware
/// registers the transaction here for the owning call site (which runs after the
/// middleware's flow is gone) to <see cref="Take"/> and complete.</description></item>
/// </list>
/// The owning call site (MessageDelivery.InvokeAndDispatch / the Mediator saga branch)
/// takes the transaction, re-establishes the ambient in its own context, stages the
/// cascades through the dispatch path, and commits or rolls back in a <c>finally</c>.
/// </summary>
internal static class OutboxTransactionScope
{
    private static readonly AsyncLocal<Scope?> _scope = new();
    private static readonly ConcurrentDictionary<EnvelopeId, IOutboxTransaction> _registered = new();

    /// <summary>
    /// The transaction in flight for the current handler attempt, if any. Only reliable in
    /// the flow that set it — the middleware's attempt flow and the call site's dispatch
    /// flow (after it re-begins the taken transaction).
    /// </summary>
    public static IOutboxTransaction? Current => _scope.Value?.Transaction;

    /// <summary>
    /// Set <paramref name="transaction"/> as the ambient scope, remembering the prior
    /// scope. Dispose the returned handle (or call <see cref="End"/>) to restore it.
    /// </summary>
    public static IDisposable Begin(IOutboxTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var scope = new Scope(_scope.Value, transaction);
        _scope.Value = scope;
        return scope;
    }

    /// <summary>
    /// Restore the ambient scope that was in place before the current transaction began.
    /// </summary>
    public static void End() => _scope.Value?.Dispose();

    /// <summary>
    /// Register the middleware's transaction under the handler envelope id so the owning
    /// call site can pick it up after the executor returns. The middleware unregisters on
    /// the rollback path; the call site removes the entry with <see cref="Take"/>.
    /// </summary>
    public static void Register(EnvelopeId id, IOutboxTransaction transaction) =>
        _registered[id] = transaction;

    /// <summary>
    /// Drop a registration — the middleware's rollback path, so a failed attempt never
    /// leaves an orphaned entry for the call site to commit.
    /// </summary>
    public static void Unregister(EnvelopeId id) => _registered.TryRemove(id, out _);

    /// <summary>
    /// Remove and return the transaction registered for the handler envelope — the owning
    /// call site consumes it after the handler attempt and completes it in a <c>finally</c>.
    /// </summary>
    public static IOutboxTransaction? Take(EnvelopeId id) =>
        _registered.TryRemove(id, out IOutboxTransaction? transaction) ? transaction : null;

    /// <summary>
    /// Commit (success) or roll back (failure) the taken transaction and restore the
    /// ambient scope. A transaction the handler already completed explicitly is not
    /// double-touched; any other exception is a real failure and propagates.
    /// </summary>
    public static async Task Complete(IOutboxTransaction transaction, bool success, CancellationToken ct)
    {
        try
        {
            if (success)
            {
                try
                {
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                }
                catch (OutboxTransactionAlreadyCompletedException)
                {
                }
            }
            else
            {
                try
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                }
                catch (OutboxTransactionAlreadyCompletedException)
                {
                }
            }
        }
        finally
        {
            End();
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly Scope? _prior;

        public Scope(Scope? prior, IOutboxTransaction transaction)
        {
            _prior = prior;
            Transaction = transaction;
        }

        public IOutboxTransaction Transaction { get; }

        public void Dispose() => _scope.Value = _prior;
    }
}