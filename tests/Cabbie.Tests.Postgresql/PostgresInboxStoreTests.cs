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

        Assert.True(await _store.TryAcquireAsync(id));
        Assert.False(await _store.TryAcquireAsync(id));
    }

    [Fact]
    public async Task inbox_release_then_try_acquire_returns_true_again()
    {
        var id = new EnvelopeId(Guid.NewGuid());
        Assert.True(await _store.TryAcquireAsync(id));

        await _store.ReleaseAsync(id);

        Assert.True(await _store.TryAcquireAsync(id));
    }

    [Fact]
    public async Task inbox_mark_processed_then_try_acquire_returns_false()
    {
        var id = new EnvelopeId(Guid.NewGuid());
        Assert.True(await _store.TryAcquireAsync(id));

        await _store.MarkProcessedAsync(id);

        Assert.False(await _store.TryAcquireAsync(id));
        Assert.Empty(await _store.LoadInFlightAsync());
    }

    [Fact]
    public async Task inbox_mark_processed_removes_id_from_load_in_flight()
    {
        var id = new EnvelopeId(Guid.NewGuid());
        Assert.True(await _store.TryAcquireAsync(id));

        await _store.MarkProcessedAsync(id);

        Assert.Empty(await _store.LoadInFlightAsync());
    }

    [Fact]
    public async Task inbox_load_in_flight_returns_acquired_but_unmarked_ids_only()
    {
        var inFlight = new EnvelopeId(Guid.NewGuid());
        var processed = new EnvelopeId(Guid.NewGuid());
        Assert.True(await _store.TryAcquireAsync(inFlight));
        Assert.True(await _store.TryAcquireAsync(processed));

        await _store.MarkProcessedAsync(processed);

        EnvelopeId[] inFlightIds = (await _store.LoadInFlightAsync()).ToArray();
        Assert.Contains(inFlight, inFlightIds);
        Assert.DoesNotContain(processed, inFlightIds);
    }

    [Fact]
    public async Task inbox_load_in_flight_after_connection_reset_returns_acquired_but_unmarked()
    {
        var id = new EnvelopeId(Guid.NewGuid());
        Assert.True(await _store.TryAcquireAsync(id));

        // Open a brand-new connection (and a brand-new store) against the same database;
        // a real recovery sees the acquired id, an in-memory store would not.
        await using NpgsqlDataSource freshDataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).Build();
        PostgresInboxStore recovered = new PostgresInboxStore(freshDataSource);

        EnvelopeId recoveredId = Assert.Single(await recovered.LoadInFlightAsync());
        Assert.Equal(id, recoveredId);
    }
}