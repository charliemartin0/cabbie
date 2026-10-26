using System.Collections.Concurrent;

namespace GettingStarted;

/// <summary>Collects the ids of orders whose OrderPlaced cascade was handled.</summary>
public sealed class OrderLog
{
    private readonly ConcurrentQueue<int> _placed = new();

    public IReadOnlyList<int> Placed => [.. _placed];

    public void Record(int orderId) => _placed.Enqueue(orderId);
}

/// <summary>Handles PlaceOrder; the returned OrderPlaced is cascaded as a new message.</summary>
public sealed class PlaceOrderHandler
{
    public OrderPlaced Handle(PlaceOrder message) => new(message.OrderId);
}

public sealed class OrderPlacedHandler(OrderLog log)
{
    public void Handle(OrderPlaced message) => log.Record(message.OrderId);
}
