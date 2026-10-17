namespace Cabbie.Domain.Envelope.ValueObjects;

/// <summary>
/// Envelope.DeliverBy. Optional deadline / scheduled execution time ([Timeout], Delay).
/// </summary>
public record DeliverBy(DateTimeOffset? Value);
