namespace Cabbie.EntityFrameworkCore;

/// <summary>
/// One outgoing envelope parked in the caller's database. <see cref="Id"/> is the envelope id, so a
/// re-send keeps the identity the downstream inbox dedupes on. <see cref="SentAt"/> is null until
/// the relay has handed the envelope to its transport.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public string Destination { get; set; } = "";

    public string MessageType { get; set; } = "";

    /// <summary>JSON object of the transport headers.</summary>
    public string Headers { get; set; } = "{}";

    public byte[] Body { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>
    /// Set by the relay when the row cannot be decoded or after <see cref="EfOutboxRelayOptions.MaxAttempts"/>
    /// failed sends; the relay never selects it again until a redrive clears it. Callers add a
    /// nullable timestamp column for it in their migration.
    /// </summary>
    public DateTimeOffset? DeadLetteredAt { get; set; }

    /// <summary>Failed send attempts so far. Callers add a non-null int column (default 0).</summary>
    public int Attempts { get; set; }

    /// <summary>Earliest time the relay retries after a failed send; null means eligible now. Nullable timestamp column.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
}
