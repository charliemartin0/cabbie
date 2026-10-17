using Cabbie.Application.Cascades;
using Cabbie.Domain.Sagas;
using Helpdesk.Domain;

namespace Helpdesk.Application.Sagas;

/// <summary>
/// One in-flight order. Start places the order and cascades both a ChargePayment
/// (immediate) and an OrderTimeout (scheduled at PlaceOrder.DueAt via
/// ScheduledCascade). The payment confirmation is an EXTERNAL event: when it arrives,
/// Handle(PaymentCharged) completes the saga; when it never arrives, the OrderTimeout
/// fires while the saga is still in progress and Handle(OrderTimeout) cancels it.
/// All methods are instance — Cabbie forbids static saga methods.
/// </summary>
public sealed class OrderSaga : Saga
{
    public OrderId? Id { get; set; }

    public CustomerId? Customer { get; set; }

    public decimal Amount { get; set; }

    public PaymentReference? PaymentRef { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Placed;

    public IReadOnlyList<object> Start(PlaceOrder message)
    {
        Id = message.Id;
        Customer = message.Customer;
        Amount = message.Amount;
        Status = OrderStatus.Placed;
        return
        [
            new ChargePayment(
                message.Id,
                new PaymentReference(Guid.NewGuid().ToString("N")),
                message.Amount),
            new ScheduledCascade(new OrderTimeout(message.Id), message.DueAt)
        ];
    }

    public void Handle(ChargePayment message)
    {
        PaymentRef = message.PaymentRef;
        Status = OrderStatus.Charged;
    }

    public void Handle(PaymentCharged message)
    {
        Status = OrderStatus.CompletedByPayment;
        MarkCompleted();
    }

    public void Handle(OrderTimeout message)
    {
        Status = OrderStatus.CompletedByTimeout;
        MarkCompleted();
    }
}