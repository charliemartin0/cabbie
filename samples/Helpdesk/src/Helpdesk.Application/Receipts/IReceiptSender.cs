using Helpdesk.Domain;

namespace Helpdesk.Application.Receipts;

/// <summary>
/// Port the receipt handler depends on; Infrastructure supplies the implementation.
/// </summary>
public interface IReceiptSender
{
    void Send(OrderId id);
}
