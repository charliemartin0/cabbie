using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Tests.Application.Persistence;

public sealed class CompositeMessageStoreTests
{
    [Fact]
    public void composite_outbox_returns_injected_outbox()
    {
        var outbox = new RecordingOutboxStore();
        var store = new CompositeMessageStore(outbox, new RecordingInboxStore(), new RecordingDeadLetterStore());

        Assert.Same(outbox, store.Outbox);
    }

    [Fact]
    public void composite_inbox_returns_injected_inbox()
    {
        var inbox = new RecordingInboxStore();
        var store = new CompositeMessageStore(new RecordingOutboxStore(), inbox, new RecordingDeadLetterStore());

        Assert.Same(inbox, store.Inbox);
    }

    [Fact]
    public void composite_dead_letter_returns_injected_dead_letter()
    {
        var deadLetter = new RecordingDeadLetterStore();
        var store = new CompositeMessageStore(new RecordingOutboxStore(), new RecordingInboxStore(), deadLetter);

        Assert.Same(deadLetter, store.DeadLetter);
    }

    [Fact]
    public void composite_implements_imessage_store()
    {
        IMessageStore store = new CompositeMessageStore(
            new RecordingOutboxStore(),
            new RecordingInboxStore(),
            new RecordingDeadLetterStore());

        Assert.NotNull(store.Outbox);
        Assert.NotNull(store.Inbox);
        Assert.NotNull(store.DeadLetter);
    }

    private sealed class RecordingOutboxStore : IOutboxStore
    {
        public IOutboxTransaction BeginOutboxTransaction() =>
            throw new NotSupportedException(
                "RecordingOutboxStore.BeginOutboxTransaction() is not implemented in this test double.");

        public ValueTask StageAsync(Envelope envelope, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public ValueTask CommitAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public ValueTask RollbackAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public ValueTask<IReadOnlyList<Envelope>> LoadPendingAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public ValueTask MarkSentAsync(EnvelopeId id, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class RecordingInboxStore : IInboxStore
    {
        public ValueTask<bool> TryAcquireAsync(EnvelopeId id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public ValueTask MarkProcessedAsync(EnvelopeId id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public ValueTask ReleaseAsync(EnvelopeId id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public ValueTask<IReadOnlyList<EnvelopeId>> LoadInFlightAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class RecordingDeadLetterStore : IDeadLetterStore
    {
        public ValueTask RecordAsync(Envelope envelope, Exception cause, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public ValueTask<IReadOnlyList<DeadLetter>> ListAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}