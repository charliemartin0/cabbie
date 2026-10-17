namespace Cabbie.Domain.Messaging.ValueObjects;

/// <summary>
/// The CLR body (PlaceOrder, ChargePayment, …), not JSON. Envelope carries this.
/// </summary>
public record Message(object Value);
