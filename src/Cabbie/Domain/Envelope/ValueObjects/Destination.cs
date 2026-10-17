namespace Cabbie.Domain.Envelope.ValueObjects;

/// <summary>
/// Envelope.Destination. Where this envelope should be delivered (local://payments/, later rabbitmq://).
/// </summary>
public record Destination(Uri Value);
