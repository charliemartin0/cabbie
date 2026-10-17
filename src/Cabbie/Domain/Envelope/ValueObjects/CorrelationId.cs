namespace Cabbie.Domain.Envelope.ValueObjects;

/// <summary>
/// Envelope.CorrelationId. Ties log lines and traces across hops. Often copied from the parent envelope.
/// </summary>
public record CorrelationId(Guid Value);
