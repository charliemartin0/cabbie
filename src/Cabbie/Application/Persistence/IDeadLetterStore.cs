using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Persistence;

/// <summary>
/// A dead-lettered envelope and the cause that put it there.
/// </summary>
/// <param name="Envelope">The envelope that exhausted its retries.</param>
/// <param name="Cause">The exception that moved the envelope to the dead-letter store.</param>
/// <param name="RecordedAt">The instant the entry was recorded.</param>
public sealed record DeadLetter(Envelope Envelope, Exception Cause, DateTimeOffset RecordedAt);

/// <summary>
/// Dead-letter port. Records envelopes that exhausted their retries and lists them for
/// inspection. Persistence owns the durable rows later; the in-memory store is the current default.
/// </summary>
public interface IDeadLetterStore
{
    /// <summary>
    /// Record an envelope that exhausted its retries together with the cause.
    /// </summary>
    ValueTask RecordAsync(Envelope envelope, Exception cause, CancellationToken ct = default);

    /// <summary>
    /// List every recorded dead letter.
    /// </summary>
    ValueTask<IReadOnlyList<DeadLetter>> ListAsync(CancellationToken ct = default);
}
