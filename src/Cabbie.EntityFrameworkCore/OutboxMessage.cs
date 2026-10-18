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
    /// Set by the relay when the row cannot be decoded; the relay never selects it again. Callers add a
    /// nullable timestamp column for it in their migration.
    /// </summary>
    public DateTimeOffset? DeadLetteredAt { get; set; }
}
