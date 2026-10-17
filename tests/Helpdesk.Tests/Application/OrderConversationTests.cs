using Helpdesk.Application.Receipts;
using Helpdesk.Application.Sagas;
using Helpdesk.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Cabbie;
using Cabbie.Application.Bus;
using Cabbie.Application.Sagas;
using Cabbie.Application.Tracking;
using Cabbie.Domain.Sagas.ValueObjects;

namespace Helpdesk.Tests.Application;

/// <summary>
/// Prove-with for the Helpdesk sample: PlaceOrder starts an OrderSaga and cascades a
/// ChargePayment; PaymentCharged is an EXTERNAL confirmation published separately, and
/// the OrderTimeout (scheduled at PlaceOrder.DueAt via ScheduledCascade) cancels the
/// in-progress saga when the payment never arrives.
/// </summary>
public sealed class OrderConversationTests
{
    [Fact]
    public async Task invoke_tracked_place_order_cascades_to_charge_payment()
    {
        using IHost host = await StartedHostAsync();
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

        TrackedSession session = await bus.InvokeTrackedAsync(NewPlaceOrder());

        Assert.Contains(session.Executed, executed => executed.Message is PlaceOrder);
        Assert.Contains(session.Executed, executed => executed.Message is ChargePayment);
    }

    [Fact]
    public async Task invoke_tracked_full_conversation_includes_payment_charged()
    {
        using IHost host = await StartedHostAsync();
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

        // The order is placed and charged; the saga is still in progress.
        TrackedSession placed = await bus.InvokeTrackedAsync(NewPlaceOrder());
        Assert.Equal(
            [typeof(PlaceOrder), typeof(ChargePayment)],
            placed.Executed.Select(executed => executed.Message.GetType()));

        // The payment confirmation arrives later, as an external event.
        TrackedSession paid = await bus.InvokeTrackedAsync(
            new PaymentCharged(new OrderId(1), new PaymentReference("pay-1"), DateTimeOffset.UtcNow));

        Assert.Equal([typeof(PaymentCharged)], paid.Executed.Select(executed => executed.Message.GetType()));

        ISagaStore store = host.Services.GetRequiredService<ISagaStore>();
        var saga = Assert.IsType<OrderSaga>(store.Load(typeof(OrderSaga), new SagaId("1")));
        Assert.Equal(OrderStatus.CompletedByPayment, saga.Status);
    }

    [Fact]
    public async Task invoke_tracked_place_order_fires_order_timeout_via_handle_within_a_short_window()
    {
        using IHost host = await StartedHostAsync();
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
        ISagaStore store = host.Services.GetRequiredService<ISagaStore>();

        // Payment never arrives; the timeout is due 100ms out.
        TrackedSession placed = await bus.InvokeTrackedAsync(
            NewPlaceOrder(dueAt: DateTimeOffset.UtcNow.AddMilliseconds(100)));
        Assert.Equal(
            [typeof(PlaceOrder), typeof(ChargePayment)],
            placed.Executed.Select(executed => executed.Message.GetType()));

        await Task.Delay(200);

        TrackedSession timedOut = await placed.PlayScheduledMessagesAsync(DateTimeOffset.UtcNow);

        // The timeout fired through the Handle path on the in-progress saga, cancelling it.
        Assert.Equal([typeof(OrderTimeout)], timedOut.Executed.Select(executed => executed.Message.GetType()));
        var saga = Assert.IsType<OrderSaga>(store.Load(typeof(OrderSaga), new SagaId("1")));
        Assert.Equal(OrderStatus.CompletedByTimeout, saga.Status);
    }

    [Fact]
    public async Task send_order_receipt_resolves_sender_from_di()
    {
        var sender = new RecordingReceiptSender();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IReceiptSender>(sender);
        builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(SendOrderReceiptHandler).Assembly));
        using IHost host = builder.Build();
        await host.StartAsync();

        await host.Services.GetRequiredService<IMessageBus>().InvokeAsync(new SendOrderReceipt(new OrderId(1)));

        Assert.Equal([new OrderId(1)], sender.Sent);
    }

    private static async Task<IHost> StartedHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options =>
        {
            options.HandlerAssemblies.Add(typeof(PlaceOrder).Assembly);
            options.HandlerAssemblies.Add(typeof(OrderSaga).Assembly);
        });
        IHost host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static PlaceOrder NewPlaceOrder(DateTimeOffset? dueAt = null) =>
        new(
            new OrderId(1),
            new CustomerId(Guid.NewGuid()),
            42.5m,
            DateTimeOffset.UtcNow,
            dueAt ?? DateTimeOffset.UtcNow.AddMinutes(5));
}

public sealed class RecordingReceiptSender : IReceiptSender
{
    public List<OrderId> Sent { get; } = [];

    public void Send(OrderId id) => Sent.Add(id);
}