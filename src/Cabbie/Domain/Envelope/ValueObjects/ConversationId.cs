namespace Cabbie.Domain.Envelope.ValueObjects;

/// <summary>
/// Envelope.ConversationId. Groups PlaceOrder + ChargePayment retries + PaymentCharged as one conversation.
/// </summary>
public record ConversationId(Guid Value);
