using System.Globalization;

namespace Helpdesk.Domain;

/// <summary>
/// Order identity. ToString yields the bare value so saga correlation produces
/// readable saga ids ("1", not "OrderId { Value = 1 }").
/// </summary>
public sealed record OrderId(int Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public sealed record CustomerId(Guid Value);

public sealed record PaymentReference(string Value);

/// <summary>
/// Starts an order conversation. Saga correlation uses the Id property (the
/// convention fallback — Helpdesk.Domain carries no Cabbie attributes).
/// DueAt is the deadline by which PaymentCharged must arrive; the OrderTimeout
/// scheduled at DueAt cancels the saga when the payment never comes.
/// </summary>
public sealed record PlaceOrder(
    OrderId Id,
    CustomerId Customer,
    decimal Amount,
    DateTimeOffset PlacedAt,
    DateTimeOffset DueAt);

public sealed record ChargePayment(OrderId Id, PaymentReference PaymentRef, decimal Amount);

public sealed record PaymentCharged(OrderId Id, PaymentReference PaymentRef, DateTimeOffset ChargedAt);

/// <summary>
/// Defined for the saga timeout path; scheduled per-order via ScheduledCascade at
/// PlaceOrder.DueAt (not via a per-type [Timeout] attribute).
/// </summary>
public sealed record OrderTimeout(OrderId Id);

/// <summary>
/// Asks for an order receipt to be sent. Handled by a handler with a constructor
/// dependency, to show handlers resolving services from the per-message DI scope.
/// </summary>
public sealed record SendOrderReceipt(OrderId Id);

/// <summary>
/// Order lifecycle. Distinguishes a saga completed by the payment confirmation from
/// one cancelled by the OrderTimeout so the sample can prove which path ran.
/// </summary>
public enum OrderStatus
{
    Placed,
    Charged,
    CompletedByPayment,
    CompletedByTimeout,
}