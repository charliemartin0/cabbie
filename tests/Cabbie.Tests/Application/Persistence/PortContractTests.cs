using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Infrastructure.Persistence;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Persistence;

public sealed class PortContractTests
{
    private static readonly HandlerName Handler = new("orders.handle");

    [Fact]
    public async Task outbox_stage_commit_then_load_pending_returns_the_envelope()
    {
        IMessageStore store = new InMemoryMessageStore();
        Envelope envelope = EnvelopeFactory.Create();

        await store.Outbox.StageAsync(envelope);
        await store.Outbox.CommitAsync();

        Envelope pending = Assert.Single(await store.Outbox.LoadPendingAsync());
        Assert.Equal(envelope.Id, pending.Id);
    }

    [Fact]
    public async Task outbox_stage_then_rollback_then_load_pending_returns_empty()
    {
        IMessageStore store = new InMemoryMessageStore();

        await store.Outbox.StageAsync(EnvelopeFactory.Create());
        await store.Outbox.RollbackAsync();

        Assert.Empty(await store.Outbox.LoadPendingAsync());
    }

    [Fact]
    public async Task outbox_mark_sent_removes_envelope_from_pending()
    {
        IMessageStore store = new InMemoryMessageStore();
        Envelope envelope = EnvelopeFactory.Create();
        await store.Outbox.StageAsync(envelope);
        await store.Outbox.CommitAsync();

        await store.Outbox.MarkSentAsync(envelope.Id);

        Assert.Empty(await store.Outbox.LoadPendingAsync());
    }

    [Fact]
    public async Task inbox_try_acquire_returns_true_first_time_false_second_time()
    {
        IMessageStore store = new InMemoryMessageStore();
        var id = new EnvelopeId(Guid.NewGuid());

        Assert.True(await store.Inbox.TryAcquireAsync(id, Handler));
        Assert.False(await store.Inbox.TryAcquireAsync(id, Handler));
    }

    [Fact]
    public async Task inbox_release_then_try_acquire_returns_true_again()
    {
        IMessageStore store = new InMemoryMessageStore();
        var id = new EnvelopeId(Guid.NewGuid());
        Assert.True(await store.Inbox.TryAcquireAsync(id, Handler));

        await store.Inbox.ReleaseAsync(id, Handler);

        Assert.True(await store.Inbox.TryAcquireAsync(id, Handler));
    }

    [Fact]
    public async Task inbox_mark_processed_then_try_acquire_returns_false()
    {
        IMessageStore store = new InMemoryMessageStore();
        var id = new EnvelopeId(Guid.NewGuid());
        Assert.True(await store.Inbox.TryAcquireAsync(id, Handler));

        await store.Inbox.MarkProcessedAsync(id, Handler);

        Assert.False(await store.Inbox.TryAcquireAsync(id, Handler));
        Assert.Empty(await store.Inbox.LoadInFlightAsync());
    }

    [Fact]
    public async Task dead_letter_record_then_list_returns_the_entry()
    {
        IMessageStore store = new InMemoryMessageStore();
        Envelope envelope = EnvelopeFactory.Create();
        var cause = new InvalidOperationException("boom");

        await store.DeadLetter.RecordAsync(envelope, cause);

        DeadLetter entry = Assert.Single(await store.DeadLetter.ListAsync());
        Assert.Equal(envelope.Id, entry.Envelope.Id);
        Assert.Same(cause, entry.Cause);
    }

    [Fact]
    public void message_store_exposes_outbox_inbox_and_dead_letter()
    {
        IMessageStore store = new InMemoryMessageStore();

        Assert.NotNull(store.Outbox);
        Assert.NotNull(store.Inbox);
        Assert.NotNull(store.DeadLetter);
    }
}
