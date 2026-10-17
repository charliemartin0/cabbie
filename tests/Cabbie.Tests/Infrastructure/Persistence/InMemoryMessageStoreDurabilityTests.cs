using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;
using Cabbie.Infrastructure.Persistence;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Infrastructure.Persistence;

public sealed class InMemoryMessageStoreDurabilityTests
{
    [Fact]
    public async Task pending_outbox_survives_a_new_store_instance_with_shared_state_via_constructor_seed()
    {
        var original = new InMemoryMessageStore();
        Envelope envelope = EnvelopeFactory.Create();
        await original.Outbox.StageAsync(envelope);
        await original.Outbox.CommitAsync();

        var rebuilt = new InMemoryMessageStore(await original.Outbox.LoadPendingAsync());

        Envelope recovered = Assert.Single(await rebuilt.Outbox.LoadPendingAsync());
        Assert.Equal(envelope.Id, recovered.Id);
    }

    [Fact]
    public async Task transaction_commit_moves_staged_to_pending()
    {
        var store = new InMemoryMessageStore();
        Envelope envelope = EnvelopeFactory.Create();
        IOutboxTransaction transaction = store.BeginOutboxTransaction();

        await transaction.StageAsync(envelope);
        await transaction.CommitAsync();

        Envelope pending = Assert.Single(await store.Outbox.LoadPendingAsync());
        Assert.Equal(envelope.Id, pending.Id);
    }

    [Fact]
    public async Task transaction_rollback_discards_staged()
    {
        var store = new InMemoryMessageStore();
        IOutboxTransaction transaction = store.BeginOutboxTransaction();
        await transaction.StageAsync(EnvelopeFactory.Create());

        await transaction.RollbackAsync();

        Assert.Empty(await store.Outbox.LoadPendingAsync());
    }

    [Fact]
    public async Task transaction_stage_after_commit_throws()
    {
        var store = new InMemoryMessageStore();
        Envelope envelope = EnvelopeFactory.Create();
        IOutboxTransaction transaction = store.BeginOutboxTransaction();
        await transaction.StageAsync(envelope);
        await transaction.CommitAsync();

        await Assert.ThrowsAsync<OutboxTransactionAlreadyCompletedException>(
            async () => await transaction.StageAsync(envelope));
    }

    [Fact]
    public async Task outbox_transactions_do_not_share_staged_buffer()
    {
        var store = new InMemoryMessageStore();
        IOutboxTransaction first = store.BeginOutboxTransaction();
        IOutboxTransaction second = store.BeginOutboxTransaction();
        Envelope firstEnvelope = EnvelopeFactory.Create();
        Envelope secondEnvelope = EnvelopeFactory.Create();

        await first.StageAsync(firstEnvelope);
        await first.RollbackAsync();
        await second.StageAsync(secondEnvelope);
        await second.CommitAsync();

        // The first transaction's rollback must not discard the second transaction's
        // staged envelope — each transaction owns its staged buffer.
        Envelope pending = Assert.Single(await store.Outbox.LoadPendingAsync());
        Assert.Equal(secondEnvelope.Id, pending.Id);
    }

    [Fact]
    public async Task outbox_transaction_double_commit_throws_already_completed_exception()
    {
        var store = new InMemoryMessageStore();
        IOutboxTransaction transaction = store.BeginOutboxTransaction();
        await transaction.StageAsync(EnvelopeFactory.Create());
        await transaction.CommitAsync();

        await Assert.ThrowsAsync<OutboxTransactionAlreadyCompletedException>(
            async () => await transaction.CommitAsync());
    }
}
