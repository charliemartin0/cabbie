using Cabbie;
using Cabbie.Application.Bus;
using Cabbie.Application.Execution;
using Cabbie.Application.Persistence;
using Cabbie.Application.Sagas;
using Cabbie.Domain.Sagas;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Postgresql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Cabbie.Tests.Postgresql;

/// <summary>
/// Starts a saga instance through the host; the mediator saves it via ISagaStore,
/// which UsePostgres has replaced with the Postgres store.
/// </summary>
internal sealed record StartHostSaga([property: SagaIdentity] string SagaId);

internal sealed class PostgresHostSaga : Saga
{
    public string Token { get; set; } = "";

    public void Start(StartHostSaga message)
    {
        Token = "started:" + message.SagaId;
    }
}

/// <summary>
/// Always throws; drives the MoveToErrorQueue policy into the Postgres dead-letter table.
/// </summary>
internal sealed record AlwaysFailsCommand();

internal sealed class AlwaysFailsHandler
{
    public void Handle(AlwaysFailsCommand _) =>
        throw new InvalidOperationException("intentional");
}

/// <summary>
/// Prove-with for the UsePostgres DI slice: the adapter extension must actually replace
/// the in-memory defaults (last-wins registration), not add alongside them, and the
/// composed Postgres stores must be reachable through the host's public surface.
/// </summary>
public sealed class PostgresHostBuilderTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;

    public PostgresHostBuilderTests(PostgresContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetSagasAsync();
        await _fixture.ResetDeadLetterAsync();
        await _fixture.ResetScheduledAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void use_postgres_overrides_in_memory_defaults()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie();
        builder.UsePostgres(_fixture.ConnectionString);
        using IHost host = builder.Build();

        // The composite, not InMemoryMessageStore; the Postgres store, not InMemorySagaStore.
        Assert.IsType<CompositeMessageStore>(host.Services.GetRequiredService<IMessageStore>());
        Assert.IsType<PostgresSagaStore>(host.Services.GetRequiredService<ISagaStore>());
    }

    [Fact]
    public async Task use_postgres_saga_state_persists_in_postgres_after_handler_runs()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(PostgresHostSaga).Assembly));
        builder.UsePostgres(_fixture.ConnectionString);
        using IHost host = builder.Build();
        try
        {
            IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
            await bus.InvokeAsync(new StartHostSaga("saga-1"));

            // The mediator saved through the host's ISagaStore, which UsePostgres replaced.
            var saga = Assert.IsType<PostgresHostSaga>(
                host.Services.GetRequiredService<ISagaStore>().Load(typeof(PostgresHostSaga), new SagaId("saga-1")));
            Assert.Equal("started:saga-1", saga.Token);

            // Open a brand-new data source and store against the same database; the saga
            // must survive the host entirely — an in-memory store would not.
            await using NpgsqlDataSource freshDataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).Build();
            PostgresSagaStore recovered = new(freshDataSource);
            var reloaded = Assert.IsType<PostgresHostSaga>(
                recovered.Load(typeof(PostgresHostSaga), new SagaId("saga-1")));
            Assert.Equal("started:saga-1", reloaded.Token);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task use_postgres_dead_letter_records_after_move_to_error_queue()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options =>
        {
            options.HandlerAssemblies.Add(typeof(AlwaysFailsHandler).Assembly);
            options.OnException<InvalidOperationException>().MoveToErrorQueue();
        });
        builder.UsePostgres(_fixture.ConnectionString);
        using IHost host = builder.Build();
        try
        {
            IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
            IDeadLetterStore store = host.Services.GetRequiredService<IDeadLetterStore>();

            await Assert.ThrowsAsync<HandlerFault>(() => bus.InvokeAsync(new AlwaysFailsCommand()));

            // IErrorQueue.MoveAsync is awaited by the executor, so the row is already written
            // by the time InvokeAsync throws.
            DeadLetter dead = Assert.Single(await store.ListAsync());

            // Message.Value is stored as JSON and does not round-trip to the CLR type (the port
            // contract only guarantees the envelope id and the cause survive); assert the stable
            // wire name and the reconstructed cause instead.
            Assert.EndsWith("AlwaysFailsCommand", dead.Envelope.MessageType.Value);
            Assert.IsType<InvalidOperationException>(dead.Cause);
            Assert.Equal("intentional", dead.Cause.Message);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task ensure_all_async_creates_all_four_tables()
    {
        await using NpgsqlDataSource dataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).Build();

        // Drop first so existence is proven by EnsureAllAsync, not by the fixture bootstrap.
        await DropTableAsync(dataSource, "cabbie_outbox");
        await DropTableAsync(dataSource, "cabbie_inbox");
        await DropTableAsync(dataSource, "cabbie_dead_letter");
        await DropTableAsync(dataSource, "cabbie_sagas");

        await PostgresSchema.EnsureAllAsync(dataSource);

        HashSet<string> tables = await ListTablesAsync(dataSource);
        Assert.Contains("cabbie_outbox", tables);
        Assert.Contains("cabbie_inbox", tables);
        Assert.Contains("cabbie_dead_letter", tables);
        Assert.Contains("cabbie_sagas", tables);
    }

    [Fact]
    public async Task ensure_all_async_is_idempotent()
    {
        await using NpgsqlDataSource dataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).Build();

        await PostgresSchema.EnsureAllAsync(dataSource);
        await PostgresSchema.EnsureAllAsync(dataSource);
    }

    private static async Task DropTableAsync(NpgsqlDataSource dataSource, string table)
    {
        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new NpgsqlCommand($"DROP TABLE IF EXISTS {table}", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<HashSet<string>> ListTablesAsync(NpgsqlDataSource dataSource)
    {
        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'", conn);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
        var tables = new HashSet<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}