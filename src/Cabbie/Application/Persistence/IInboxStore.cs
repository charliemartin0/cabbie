using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Application.Persistence;

/// <summary>
/// Inbox port. Acquires an incoming envelope for processing exactly once, then marks it
/// processed or releases it for retry. Persistence owns the durable rows later; the
/// in-memory store is the current default.
/// </summary>
public interface IInboxStore
{
    /// <summary>
    /// Try to acquire an envelope for processing. Returns false if the id was already
    /// processed or is in-flight, giving duplicate-id idempotency.
    /// </summary>
    ValueTask<bool> TryAcquireAsync(EnvelopeId id, CancellationToken ct = default);

    /// <summary>
    /// Mark an acquired envelope as successfully processed.
    /// </summary>
    ValueTask MarkProcessedAsync(EnvelopeId id, CancellationToken ct = default);

    /// <summary>
    /// Release an acquired envelope back to in-flight so a retry can acquire it again.
    /// </summary>
    ValueTask ReleaseAsync(EnvelopeId id, CancellationToken ct = default);

    /// <summary>
    /// Load ids acquired but not yet marked processed, for recovery on host start.
    /// </summary>
    ValueTask<IReadOnlyList<EnvelopeId>> LoadInFlightAsync(CancellationToken ct = default);
}
