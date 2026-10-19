using Cabbie.Application.Bus;
using Cabbie.Application.Cascades;
using Cabbie.Application.Tracking;
using Cabbie.Domain.Sagas;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Postgresql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Cabbie.Tests.Postgresql;

internal sealed record PgConvPlaceOrder([property: SagaIdentity] int OrderId, DateTimeOffset DueAt);

internal sealed record PgConvChargePayment([property: SagaIdentity] int OrderId);

internal sealed record PgConvPaymentCharged([property: SagaIdentity] int OrderId);

internal sealed record PgConvOrderTimeout([property: SagaIdentity] int OrderId);

internal sealed class PgConvOrderSaga : Saga
{
    public string Status { get; set; } = "placed";

    public IReadOnlyList<object> Start(PgConvPlaceOrder message)
    {
        Status = "placed";
        return
        [
            new PgConvChargePayment(message.OrderId),
            new ScheduledCascade(new PgConvOrderTimeout(message.OrderId), message.DueAt)
        ];
    }

    public void Handle(PgConvChargePayment message) => Status = "charged";

    public void Handle(PgConvPaymentCharged message)
    {
        Status = "completed-by-payment";
        MarkCompleted();
    }

    public void Handle(PgConvOrderTimeout message)
    {
        Status = "completed-by-timeout";
        MarkCompleted();
    }
}

/// <summary>
/// The order conversation through a real host with <c>UsePostgres</c>: saga state
/// round-trips through a real Postgres container, read back via a fresh store.
/// </summary>
public sealed class PostgresConversationTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;

    public PostgresConversationTests(PostgresContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetSagasAsync();
        await _fixture.ResetOutboxAsync();
        await _fixture.ResetInboxAsync();
        await _fixture.ResetDeadLetterAsync();
        await _fixture.ResetScheduledAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task tracked_start_cascades_and_persists_the_charged_saga_postgres()
    {
        using IHost host = await StartedHostAsync();
        try
        {
            TrackedSession session = await host.Services.GetRequiredService<IMessageBus>()
                .InvokeTrackedAsync(NewOrder(DateTimeOffset.UtcNow.AddMinutes(5)));

            Assert.Equal(
                [typeof(PgConvPlaceOrder), typeof(PgConvChargePayment)],
                session.Executed.Select(executed => executed.Message.GetType()));
            Assert.Equal("charged", ReloadStatus());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task external_event_completes_the_persisted_saga_postgres()
    {
        using IHost host = await StartedHostAsync();
        try
        {
            IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
            await bus.InvokeTrackedAsync(NewOrder(DateTimeOffset.UtcNow.AddMinutes(5)));

            TrackedSession paid = await bus.InvokeTrackedAsync(new PgConvPaymentCharged(1));

            Assert.Equal([typeof(PgConvPaymentCharged)], paid.Executed.Select(executed => executed.Message.GetType()));
            Assert.Equal("completed-by-payment", ReloadStatus());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task scheduled_timeout_cancels_the_persisted_saga_postgres()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using IHost host = await StartedHostAsync();
        try
        {
            TrackedSession placed = await host.Services.GetRequiredService<IMessageBus>()
                .InvokeTrackedAsync(NewOrder(now.AddMinutes(1)));

            TrackedSession timedOut = await placed.PlayScheduledMessagesAsync(now.AddMinutes(2));

            Assert.Equal([typeof(PgConvOrderTimeout)], timedOut.Executed.Select(executed => executed.Message.GetType()));
            Assert.Equal("completed-by-timeout", ReloadStatus());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static PgConvPlaceOrder NewOrder(DateTimeOffset dueAt) => new(1, dueAt);

    private string ReloadStatus()
    {
        using NpgsqlDataSource fresh = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).Build();
        var recovered = new PostgresSagaStore(fresh);
        return Assert.IsType<PgConvOrderSaga>(recovered.Load(typeof(PgConvOrderSaga), new SagaId("1"))).Status;
    }

    private async Task<IHost> StartedHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(PgConvOrderSaga).Assembly));
        builder.UsePostgres(_fixture.ConnectionString);
        IHost host = builder.Build();
        await host.StartAsync();
        return host;
    }
}
