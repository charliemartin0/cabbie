using Cabbie.Application.Cascades;
using Cabbie.Domain.Sagas;
using Microsoft.Extensions.Logging;

namespace Cabbie.Tests.Handlers;

/// <summary>Starts a <see cref="ConvOrderSaga"/>; DueAt is when the timeout fires.</summary>
public sealed record ConvPlaceOrder([property: SagaIdentity] int OrderId, DateTimeOffset DueAt);

public sealed record ConvChargePayment([property: SagaIdentity] int OrderId);

/// <summary>External confirmation that arrives separately from the cascade.</summary>
public sealed record ConvPaymentCharged([property: SagaIdentity] int OrderId);

public sealed record ConvOrderTimeout([property: SagaIdentity] int OrderId);

public sealed record ConvSendReceipt(int OrderId);

public enum ConvStatus
{
    Placed,
    Charged,
    CompletedByPayment,
    CompletedByTimeout
}

public sealed class ConvOrderSaga : Saga
{
    public ConvStatus Status { get; set; } = ConvStatus.Placed;

    public IReadOnlyList<object> Start(ConvPlaceOrder message)
    {
        Status = ConvStatus.Placed;
        return
        [
            new ConvChargePayment(message.OrderId),
            new ScheduledCascade(new ConvOrderTimeout(message.OrderId), message.DueAt)
        ];
    }

    public void Handle(ConvChargePayment message) => Status = ConvStatus.Charged;

    public void Handle(ConvPaymentCharged message, ILogger<ConvOrderSaga> logger)
    {
        Status = ConvStatus.CompletedByPayment;
        ConvOrderSagaLog.Completed(logger, message.OrderId);
        MarkCompleted();
    }

    public void Handle(ConvOrderTimeout message)
    {
        Status = ConvStatus.CompletedByTimeout;
        MarkCompleted();
    }
}

internal static partial class ConvOrderSagaLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} completed by payment")]
    public static partial void Completed(ILogger logger, int orderId);
}

public sealed class ConvReceiptHandler(ILogger<ConvReceiptHandler> logger)
{
    public void Handle(ConvSendReceipt message) => ConvReceiptLog.Sent(logger, message.OrderId);
}

internal static partial class ConvReceiptLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Receipt sent for order {OrderId}")]
    public static partial void Sent(ILogger logger, int orderId);
}
