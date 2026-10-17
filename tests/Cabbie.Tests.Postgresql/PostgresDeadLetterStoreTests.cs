using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;
using Cabbie.Postgresql;
using Npgsql;

namespace Cabbie.Tests.Postgresql;

/// <summary>
/// Prove-with for the Postgres IDeadLetterStore implementation. Mirrors the in-memory
/// port-contract fact (id match, cause carries enough info to identify the failure) plus
/// the behavior only a real database can prove: recorded entries survive a connection reset.
/// </summary>
public sealed class PostgresDeadLetterStoreTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private PostgresDeadLetterStore _store = null!;

    public PostgresDeadLetterStoreTests(PostgresContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetDeadLetterAsync();
        _store = new PostgresDeadLetterStore(_fixture.DataSource);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task dead_letter_record_then_list_returns_the_entry_with_matching_envelope_id()
    {
        Envelope envelope = PostgresOutboxTestEnvelope.Create();
        var cause = new InvalidOperationException("boom");

        await _store.RecordAsync(envelope, cause);

        DeadLetter entry = Assert.Single(await _store.ListAsync());
        Assert.Equal(envelope.Id, entry.Envelope.Id);
    }

    [Fact]
    public async Task dead_letter_list_returns_empty_before_any_record()
    {
        Assert.Empty(await _store.ListAsync());
    }

    [Fact]
    public async Task dead_letter_record_preserves_cause_message_and_type()
    {
        Envelope envelope = PostgresOutboxTestEnvelope.Create();
        var cause = new InvalidOperationException("boom");

        await _store.RecordAsync(envelope, cause);

        DeadLetter entry = Assert.Single(await _store.ListAsync());
        Assert.IsType<InvalidOperationException>(entry.Cause);
        Assert.Equal("boom", entry.Cause.Message);
    }

    [Fact]
    public async Task dead_letter_list_after_connection_reset_returns_recorded_entries()
    {
        Envelope envelope = PostgresOutboxTestEnvelope.Create();
        await _store.RecordAsync(envelope, new InvalidOperationException("boom"));

        // Open a brand-new connection (and a brand-new store) against the same database;
        // a real recovery sees the recorded entry, an in-memory store would not.
        await using NpgsqlDataSource freshDataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).Build();
        PostgresDeadLetterStore recovered = new PostgresDeadLetterStore(freshDataSource);

        DeadLetter entry = Assert.Single(await recovered.ListAsync());
        Assert.Equal(envelope.Id, entry.Envelope.Id);
    }
}