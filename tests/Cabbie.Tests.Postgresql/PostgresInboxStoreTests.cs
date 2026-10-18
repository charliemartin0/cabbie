using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Postgresql;
using Npgsql;

namespace Cabbie.Tests.Postgresql;

/// <summary>
/// Prove-with for the Postgres IInboxStore implementation. Mirrors the in-memory
/// port-contract facts plus the behavior only a real database can prove: an acquired
/// id survives a connection reset and is still visible to recovery.
/// </summary>
public sealed class PostgresInboxStoreTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private static readonly HandlerName Handler = new("orders.handle");
    private PostgresInboxStore _store = null!;

    public PostgresInboxStoreTests(PostgresContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetInboxAsync();
        _store = new PostgresInboxStore(_fixture.DataSource);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task inbox_try_acquire_returns_true_first_time_false_second_time()
    {
        var id = new EnvelopeId(Guid.NewGuid());

        Assert.True(await _store.TryAcquireAsync(id, Handler));
        Assert.False(await _store.TryAcquireAsync(id, Handler));
    }

    [Fact]
    public async Task inbox_release_then_try_acquire_returns_true_again()
    {
        var id = new EnvelopeId(Guid.NewGuid());
        Assert.True(await _store.TryAcquireAsync(id, Handler));

        await _store.ReleaseAsync(id, Handler);

        Assert.True(await _store.TryAcquireAsync(id, Handler));
    }

    [Fact]
    public async Task inbox_mark_processed_then_try_acquire_returns_false()
    {
        var id = new EnvelopeId(Guid.NewGuid());
        Assert.True(await _store.TryAcquireAsync(id, Handler));

        await _store.MarkProcessedAsync(id, Handler);

        Assert.False(await _store.TryAcquireAsync(id, Handler));
        Assert.Empty(await _store.LoadInFlightAsync());
    }

    [Fact]
    public async Task inbox_mark_processed_removes_id_from_load_in_flight()
    {
        var id = new EnvelopeId(Guid.NewGuid());
        Assert.True(await _store.TryAcquireAsync(id, Handler));

        await _store.MarkProcessedAsync(id, Handler);

        Assert.Empty(await _store.LoadInFlightAsync());
    }

    [Fact]
    public async Task inbox_load_in_flight_returns_acquired_but_unmarked_ids_only()
    {
        var inFlight = new EnvelopeId(Guid.NewGuid());
        var processed = new EnvelopeId(Guid.NewGuid());
        Assert.True(await _store.TryAcquireAsync(inFlight, Handler));
        Assert.True(await _store.TryAcquireAsync(processed, Handler));

        await _store.MarkProcessedAsync(processed, Handler);

        EnvelopeId[] inFlightIds = (await _store.LoadInFlightAsync()).Select(entry => entry.Id).ToArray();
        Assert.Contains(inFlight, inFlightIds);
        Assert.DoesNotContain(processed, inFlightIds);
    }

    [Fact]
    public async Task inbox_load_in_flight_after_connection_reset_returns_acquired_but_unmarked()
    {
        var id = new EnvelopeId(Guid.NewGuid());
        Assert.True(await _store.TryAcquireAsync(id, Handler));

        // Open a brand-new connection (and a brand-new store) against the same database;
        // a real recovery sees the acquired id, an in-memory store would not.
        await using NpgsqlDataSource freshDataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).Build();
        PostgresInboxStore recovered = new PostgresInboxStore(freshDataSource);

        InboxEntry recoveredEntry = Assert.Single(await recovered.LoadInFlightAsync());
        Assert.Equal(new InboxEntry(id, Handler), recoveredEntry);
    }

    [Fact]
    public async Task inbox_two_consumers_acquiring_the_same_id_and_handler_yield_exactly_one_winner()
    {
        var id = new EnvelopeId(Guid.NewGuid());
        PostgresInboxStore[] consumers = Enumerable.Range(0, 8)
            .Select(_ => new PostgresInboxStore(_fixture.DataSource))
            .ToArray();

        bool[] results = await Task.WhenAll(
            consumers.Select(consumer => Task.Run(async () => await consumer.TryAcquireAsync(id, Handler))));

        Assert.Single(results, won => won);
    }

    [Fact]
    public async Task inbox_same_id_acquired_by_different_handlers_is_tracked_independently()
    {
        var id = new EnvelopeId(Guid.NewGuid());
        var other = new HandlerName("shipping.handle");

        Assert.True(await _store.TryAcquireAsync(id, Handler));
        Assert.True(await _store.TryAcquireAsync(id, other));
        await _store.MarkProcessedAsync(id, Handler);

        Assert.False(await _store.TryAcquireAsync(id, Handler));
        Assert.Equal(new InboxEntry(id, other), Assert.Single(await _store.LoadInFlightAsync()));
    }

    [Fact]
    public async Task inbox_release_does_not_reopen_a_processed_pair()
    {
        var id = new EnvelopeId(Guid.NewGuid());
        Assert.True(await _store.TryAcquireAsync(id, Handler));
        await _store.MarkProcessedAsync(id, Handler);

        await _store.ReleaseAsync(id, Handler);

        Assert.False(await _store.TryAcquireAsync(id, Handler));
    }

    [Fact]
    public async Task ensure_inbox_table_upgrades_an_id_only_table_and_keeps_its_rows()
    {
        var id = new EnvelopeId(Guid.NewGuid());
        await using (NpgsqlConnection conn = await _fixture.DataSource.OpenConnectionAsync())
        {
            await using NpgsqlCommand legacy = new NpgsqlCommand(
                """
                DROP TABLE cabbie_inbox;
                CREATE TABLE cabbie_inbox (
                    id UUID PRIMARY KEY,
                    status TEXT NOT NULL CHECK (status IN ('in_flight', 'processed')),
                    acquired_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );
                INSERT INTO cabbie_inbox (id, status) VALUES (@id, 'processed');
                """,
                conn);
            legacy.Parameters.AddWithValue("id", id.Value);
            await legacy.ExecuteNonQueryAsync();
        }

        await PostgresSchema.EnsureInboxTableAsync(_fixture.DataSource);
        await PostgresSchema.EnsureInboxTableAsync(_fixture.DataSource);

        Assert.False(await _store.TryAcquireAsync(id, new HandlerName(string.Empty)));
        Assert.True(await _store.TryAcquireAsync(id, Handler));
    }
}
