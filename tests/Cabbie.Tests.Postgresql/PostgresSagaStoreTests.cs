using Cabbie.Application.Sagas;
using Cabbie.Domain.Sagas;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Postgresql;
using Npgsql;

namespace Cabbie.Tests.Postgresql;

internal sealed class TokenSaga : Saga
{
    public string Token { get; set; } = "";
}

internal sealed class OtherTokenSaga : Saga
{
    public string OtherToken { get; set; } = "";
}

/// <summary>
/// Prove-with for the Postgres ISagaStore implementation. Mirrors the in-memory
/// port-contract facts (clone semantics, missing-id null, completion round-trip)
/// plus the behaviors only a real database can prove: the (saga_type, saga_id)
/// composite key isolates rows by type, upserts are last-write-wins, and a saved
/// saga survives a connection reset.
/// </summary>
public sealed class PostgresSagaStoreTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private PostgresSagaStore _store = null!;

    public PostgresSagaStoreTests(PostgresContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetSagasAsync();
        _store = new PostgresSagaStore(_fixture.DataSource);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void load_missing_id_is_null()
    {
        Assert.Null(_store.Load(typeof(TokenSaga), new SagaId("missing")));
    }

    [Fact]
    public void load_after_save_returns_the_saved_state()
    {
        var id = new SagaId("order-1");
        _store.Save(typeof(TokenSaga), id, new TokenSaga { Token = "abc" });

        var loaded = Assert.IsType<TokenSaga>(_store.Load(typeof(TokenSaga), id));
        Assert.Equal("abc", loaded.Token);
    }

    [Fact]
    public void load_returns_a_clone_not_the_saved_instance()
    {
        var saga = new TokenSaga { Token = "abc" };
        var id = new SagaId("order-1");

        _store.Save(typeof(TokenSaga), id, saga);
        saga.Token = "mutated";

        var loaded = Assert.IsType<TokenSaga>(_store.Load(typeof(TokenSaga), id));
        Assert.Equal("abc", loaded.Token);
        Assert.NotSame(saga, loaded);
    }

    [Fact]
    public void load_mutations_do_not_leak_into_subsequent_loads()
    {
        var id = new SagaId("order-1");
        _store.Save(typeof(TokenSaga), id, new TokenSaga { Token = "abc" });

        var first = Assert.IsType<TokenSaga>(_store.Load(typeof(TokenSaga), id));
        first.Token = "mutated";

        var second = Assert.IsType<TokenSaga>(_store.Load(typeof(TokenSaga), id));
        Assert.Equal("abc", second.Token);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void save_upserts_existing_row()
    {
        var id = new SagaId("order-1");
        _store.Save(typeof(TokenSaga), id, new TokenSaga { Token = "first" });

        _store.Save(typeof(TokenSaga), id, new TokenSaga { Token = "second" });

        var loaded = Assert.IsType<TokenSaga>(_store.Load(typeof(TokenSaga), id));
        Assert.Equal("second", loaded.Token);
    }

    [Fact]
    public void is_completed_round_trips_through_save_and_load()
    {
        var saga = new TokenSaga { Token = "done" };
        saga.MarkCompleted();
        var id = new SagaId("order-1");

        _store.Save(typeof(TokenSaga), id, saga);

        var loaded = Assert.IsType<TokenSaga>(_store.Load(typeof(TokenSaga), id));
        Assert.True(loaded.IsCompleted);
    }

    [Fact]
    public void load_under_wrong_saga_type_is_null()
    {
        var id = new SagaId("x");
        _store.Save(typeof(TokenSaga), id, new TokenSaga { Token = "abc" });

        Assert.Null(_store.Load(typeof(OtherTokenSaga), id));
    }

    [Fact]
    public void saga_id_string_round_trips()
    {
        _store.Save(typeof(TokenSaga), new SagaId("order-123"), new TokenSaga { Token = "abc" });

        var loaded = Assert.IsType<TokenSaga>(_store.Load(typeof(TokenSaga), new SagaId("order-123")));
        Assert.Equal("abc", loaded.Token);
    }

    [Fact]
    public void load_after_connection_reset_returns_the_saved_state()
    {
        var id = new SagaId("order-1");
        _store.Save(typeof(TokenSaga), id, new TokenSaga { Token = "durable" });

        // Open a brand-new connection (and a brand-new store) against the same database;
        // a real recovery sees the saved saga, an in-memory store would not.
        using NpgsqlDataSource freshDataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).Build();
        PostgresSagaStore recovered = new PostgresSagaStore(freshDataSource);

        var loaded = Assert.IsType<TokenSaga>(recovered.Load(typeof(TokenSaga), id));
        Assert.Equal("durable", loaded.Token);
    }
}