using Helpdesk.Application.Receipts;
using Helpdesk.Domain;
using Microsoft.Extensions.Logging;

namespace Helpdesk.Infrastructure;

public sealed class LoggingReceiptSender(ILogger<LoggingReceiptSender> logger) : IReceiptSender
{
    public void Send(OrderId id) => logger.LogInformation("Sent receipt for order {OrderId}", id);
}
