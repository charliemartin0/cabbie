using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Application.Persistence;

/// <summary>
/// Outbox port. Two stage/commit paths share the pending set: the long-lived store
/// (StageAsync/CommitAsync on this interface) is the host-replay and dispatch-recovery
/// path, while the short-lived <see cref="IOutboxTransaction"/> returned by
/// <see cref="BeginOutboxTransaction"/> is the per-handler transactional path — the
/// kernel middleware stages cascades on the transaction, never on the long-lived store.
/// Pending envelopes replay on host start; persistence owns the durable rows later; the
/// in-memory store is the current default.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Stage an outgoing envelope in the current transaction. The envelope is not yet
    /// replayable until <see cref="CommitAsync"/> succeeds.
    /// </summary>
    ValueTask StageAsync(Envelope envelope, CancellationToken ct = default);

    /// <summary>
    /// Commit staged envelopes as pending. Call after handler success.
    /// </summary>
    ValueTask CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Discard staged envelopes. Call on handler failure so a throwing handler publishes nothing.
    /// </summary>
    ValueTask RollbackAsync(CancellationToken ct = default);

    /// <summary>
    /// Load pending envelopes for replay on host start.
    /// </summary>
    ValueTask<IReadOnlyList<Envelope>> LoadPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Mark a pending envelope as sent after Execution takes ownership of it.
    /// </summary>
    ValueTask MarkSentAsync(EnvelopeId id, CancellationToken ct = default);

    /// <summary>
    /// Begin a short-lived outbox transaction, one per handler attempt. Cascaded messages
    /// MUST be staged on the returned transaction (not the long-lived store) and
    /// committed/rolled back aligned with the handler outcome — the
    /// TransactionalOutboxMiddleware opens it, the dispatch path stages on it, and the
    /// owning call site commits after the saga save and the immediate-publish attempt.
    /// </summary>
    IOutboxTransaction BeginOutboxTransaction();
}
