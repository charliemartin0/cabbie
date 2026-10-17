using Helpdesk.Application.Receipts;
using Helpdesk.Domain;
using Microsoft.Extensions.Logging;

namespace Helpdesk.Infrastructure;

public sealed partial class LoggingReceiptSender(ILogger<LoggingReceiptSender> logger) : IReceiptSender
{
    public void Send(OrderId id) => LogSent(id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent receipt for order {OrderId}")]
    private partial void LogSent(OrderId orderId);
}
