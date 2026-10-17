using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;
using Cabbie.Postgresql;

namespace Cabbie.Tests.Postgresql;

/// <summary>
/// Prove-with for the Postgres short-lived <see cref="IOutboxTransaction"/>. Mirrors the
/// in-memory transaction contract: staging is buffered locally and only becomes pending on
/// commit, a single transaction is single-use, and two transactions from the same store do
/// not share a staged buffer. The final fact proves the committed rows are durable and not
/// smuggled through the transaction's in-memory buffer.
/// </summary>
public sealed class PostgresOutboxTransactionTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private PostgresOutboxStore _store = null!;

    public PostgresOutboxTransactionTests(PostgresContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetOutboxAsync();
        _store = new PostgresOutboxStore(_fixture.DataSource);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task transaction_commit_moves_staged_to_pending()
    {
        Envelope first = PostgresOutboxTestEnvelope.Create();
        Envelope second = PostgresOutboxTestEnvelope.Create();

        IOutboxTransaction transaction = _store.BeginOutboxTransaction();
        await transaction.StageAsync(first);
        await transaction.StageAsync(second);
        await transaction.CommitAsync();

        IReadOnlyList<Envelope> pending = await _store.LoadPendingAsync();
        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, envelope => envelope.Id == first.Id);
        Assert.Contains(pending, envelope => envelope.Id == second.Id);
    }

    [Fact]
    public async Task transaction_rollback_discards_staged()
    {
        IOutboxTransaction transaction = _store.BeginOutboxTransaction();
        await transaction.StageAsync(PostgresOutboxTestEnvelope.Create());
        await transaction.StageAsync(PostgresOutboxTestEnvelope.Create());
        await transaction.RollbackAsync();

        Assert.Empty(await _store.LoadPendingAsync());
    }

    [Fact]
    public async Task transaction_stage_after_commit_throws()
    {
        IOutboxTransaction transaction = _store.BeginOutboxTransaction();
        await transaction.StageAsync(PostgresOutboxTestEnvelope.Create());
        await transaction.CommitAsync();

        await Assert.ThrowsAsync<OutboxTransactionAlreadyCompletedException>(
            async () => await transaction.StageAsync(PostgresOutboxTestEnvelope.Create()));
    }

    [Fact]
    public async Task outbox_transactions_do_not_share_staged_buffer()
    {
        Envelope first = PostgresOutboxTestEnvelope.Create();
        Envelope second = PostgresOutboxTestEnvelope.Create();

        IOutboxTransaction firstTransaction = _store.BeginOutboxTransaction();
        IOutboxTransaction secondTransaction = _store.BeginOutboxTransaction();
        await firstTransaction.StageAsync(first);
        await secondTransaction.StageAsync(second);
        await firstTransaction.RollbackAsync();
        await secondTransaction.CommitAsync();

        Envelope pending = Assert.Single(await _store.LoadPendingAsync());
        Assert.Equal(second.Id, pending.Id);
    }

    [Fact]
    public async Task outbox_transaction_double_commit_throws_already_completed_exception()
    {
        IOutboxTransaction transaction = _store.BeginOutboxTransaction();
        await transaction.StageAsync(PostgresOutboxTestEnvelope.Create());
        await transaction.CommitAsync();

        await Assert.ThrowsAsync<OutboxTransactionAlreadyCompletedException>(
            async () => await transaction.CommitAsync());
    }

    [Fact]
    public async Task transaction_committed_rows_remain_pending_across_store_instances()
    {
        Envelope first = PostgresOutboxTestEnvelope.Create();
        Envelope second = PostgresOutboxTestEnvelope.Create();

        IOutboxTransaction transaction = _store.BeginOutboxTransaction();
        await transaction.StageAsync(first);
        await transaction.StageAsync(second);
        await transaction.CommitAsync();

        // A brand-new store built from the same data source only sees the rows if the
        // transaction actually wrote them; an in-memory buffer would not survive.
        PostgresOutboxStore recovered = new PostgresOutboxStore(_fixture.DataSource);
        IReadOnlyList<Envelope> pending = await recovered.LoadPendingAsync();

        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, envelope => envelope.Id == first.Id);
        Assert.Contains(pending, envelope => envelope.Id == second.Id);
    }
}
