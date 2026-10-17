using Helpdesk.Domain;

namespace Helpdesk.Application.Receipts;

/// <summary>
/// Constructor injection demo: the sender comes from the per-message DI scope.
/// </summary>
public sealed class SendOrderReceiptHandler(IReceiptSender sender)
{
    public void Handle(SendOrderReceipt message) => sender.Send(message.Id);
}
