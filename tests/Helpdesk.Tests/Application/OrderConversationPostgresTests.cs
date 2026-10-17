using Cabbie;
using Cabbie.Application.Bus;
using Cabbie.Application.Sagas;
using Cabbie.Application.Tracking;
using Cabbie.Domain.Sagas.ValueObjects;
using Helpdesk.Application.Sagas;
using Helpdesk.Domain;
using Helpdesk.Infrastructure;
using Helpdesk.Tests.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Helpdesk.Tests.Application;

/// <summary>
/// Prove-with for the Helpdesk Postgres wiring: the same three conversation facts as
/// <see cref="OrderConversationTests"/>, but the host is built with <c>UseCabbie</c> +
/// <c>UseHelpdeskPostgres</c> so saga, inbox, outbox, and dead-letter state round-trips
/// through a real Postgres container.
/// </summary>
public sealed class OrderConversationPostgresTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;

    public OrderConversationPostgresTests(PostgresContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAllAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task invoke_tracked_place_order_cascades_to_charge_payment_postgres()
    {
        using IHost host = await StartedHostAsync();
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
        ISagaStore store = host.Services.GetRequiredService<ISagaStore>();

        TrackedSession session = await bus.InvokeTrackedAsync(NewPlaceOrder());

        Assert.Contains(session.Executed, executed => executed.Message is PlaceOrder);
        Assert.Contains(session.Executed, executed => executed.Message is ChargePayment);

        // The saga was persisted to Postgres: the ChargePayment cascade ran and the
        // saga is now charged, awaiting the external PaymentCharged confirmation.
        var saga = Assert.IsType<OrderSaga>(store.Load(typeof(OrderSaga), new SagaId("1")));
        Assert.Equal(OrderStatus.Charged, saga.Status);
    }

    [Fact]
    public async Task invoke_tracked_full_conversation_includes_payment_charged_postgres()
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
    public async Task invoke_tracked_place_order_fires_order_timeout_via_handle_within_a_short_window_postgres()
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

    private async Task<IHost> StartedHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHelpdesk();
        builder.UseCabbie(options =>
        {
            options.HandlerAssemblies.Add(typeof(PlaceOrder).Assembly);
            options.HandlerAssemblies.Add(typeof(OrderSaga).Assembly);
        });
        await builder.UseHelpdeskPostgres(_fixture.ConnectionString);
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