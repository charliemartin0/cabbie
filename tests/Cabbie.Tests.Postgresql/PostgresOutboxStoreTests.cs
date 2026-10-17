using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Postgresql;
using Npgsql;

namespace Cabbie.Tests.Postgresql;

/// <summary>
/// Prove-with for the Postgres IOutboxStore implementation. Mirrors the in-memory
/// port-contract facts plus the two behaviors only a real database can prove:
/// staged rows are invisible until commit, and committed rows survive a connection reset.
/// </summary>
public sealed class PostgresOutboxStoreTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private PostgresOutboxStore _store = null!;

    public PostgresOutboxStoreTests(PostgresContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetOutboxAsync();
        _store = new PostgresOutboxStore(_fixture.DataSource);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task outbox_stage_commit_then_load_pending_returns_the_envelope()
    {
        Envelope envelope = PostgresOutboxTestEnvelope.Create();

        await _store.StageAsync(envelope);
        await _store.CommitAsync();

        Envelope pending = Assert.Single(await _store.LoadPendingAsync());
        Assert.Equal(envelope.Id, pending.Id);
    }

    [Fact]
    public async Task outbox_stage_then_rollback_then_load_pending_returns_empty()
    {
        await _store.StageAsync(PostgresOutboxTestEnvelope.Create());
        await _store.RollbackAsync();

        Assert.Empty(await _store.LoadPendingAsync());
    }

    [Fact]
    public async Task outbox_mark_sent_removes_envelope_from_pending()
    {
        Envelope envelope = PostgresOutboxTestEnvelope.Create();
        await _store.StageAsync(envelope);
        await _store.CommitAsync();

        await _store.MarkSentAsync(envelope.Id);

        Assert.Empty(await _store.LoadPendingAsync());
    }

    [Fact]
    public async Task outbox_staged_envelope_is_not_visible_via_load_pending_before_commit()
    {
        Envelope envelope = PostgresOutboxTestEnvelope.Create();
        await _store.StageAsync(envelope);

        Assert.Empty(await _store.LoadPendingAsync());
    }

    [Fact]
    public async Task outbox_load_pending_after_connection_reset_returns_committed_envelopes()
    {
        Envelope envelope = PostgresOutboxTestEnvelope.Create();
        await _store.StageAsync(envelope);
        await _store.CommitAsync();

        // Open a brand-new connection (and a brand-new store) against the same database;
        // a real recovery sees the committed row, an in-memory store would not.
        await using NpgsqlDataSource freshDataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).Build();
        PostgresOutboxStore recovered = new PostgresOutboxStore(freshDataSource);

        Envelope pending = Assert.Single(await recovered.LoadPendingAsync());
        Assert.Equal(envelope.Id, pending.Id);
    }
}

internal static class PostgresOutboxTestEnvelope
{
    public static Envelope Create(EnvelopeId? id = null, byte[]? data = null)
    {
        return new Envelope(
            id ?? new EnvelopeId(Guid.NewGuid()),
            new Message(new PostgresTestPing(1)),
            new MessageType("test.ping"),
            new Destination(new Uri("local://test")),
            new CorrelationId(Guid.NewGuid()),
            new ConversationId(Guid.NewGuid()),
            new SagaId(""),
            new SentAt(DateTimeOffset.UtcNow),
            new DeliverBy(DateTimeOffset.UtcNow.AddHours(1)),
            new Headers(),
            new ContentType(""),
            new Attempts(1),
            data is null ? new EnvelopeData() : new EnvelopeData(data));
    }
}

internal sealed record PostgresTestPing(int N);
