using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Application.Persistence;

/// <summary>
/// Inbox port. Gives per-(envelope, handler) idempotency: a handler acquires an incoming
/// envelope before it runs, then marks it processed or releases it for retry. Two handlers
/// of one fan-out acquire independently; the same handler never runs twice for one envelope id
/// once processed.
/// </summary>
public interface IInboxStore
{
    /// <summary>
    /// Try to acquire an envelope for one handler. Returns false if that (id, handler) pair was
    /// already processed or is in flight, so exactly one concurrent caller wins.
    /// </summary>
    ValueTask<bool> TryAcquireAsync(EnvelopeId id, HandlerName handler, CancellationToken ct = default);

    /// <summary>
    /// Mark an acquired (id, handler) pair as successfully processed.
    /// </summary>
    ValueTask MarkProcessedAsync(EnvelopeId id, HandlerName handler, CancellationToken ct = default);

    /// <summary>
    /// Release an acquired (id, handler) pair so a retry can acquire it again. A processed pair stays processed.
    /// </summary>
    ValueTask ReleaseAsync(EnvelopeId id, HandlerName handler, CancellationToken ct = default);

    /// <summary>
    /// Load pairs acquired but not yet marked processed, for recovery on host start.
    /// </summary>
    ValueTask<IReadOnlyList<InboxEntry>> LoadInFlightAsync(CancellationToken ct = default);
}
