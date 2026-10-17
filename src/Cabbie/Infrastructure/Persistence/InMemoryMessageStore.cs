using System.Collections.Concurrent;
using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Infrastructure.Persistence;

/// <summary>
/// In-memory <see cref="IMessageStore"/> for tests and single-process hosts. Backs the outbox,
/// inbox, and dead-letter ports with concurrent dictionaries so access is thread-safe without locks.
/// </summary>
public sealed class InMemoryMessageStore : IMessageStore
{
    private readonly ConcurrentDictionary<EnvelopeId, Envelope> _staged = new();
    private readonly ConcurrentDictionary<EnvelopeId, Envelope> _pending = new();
    private readonly ConcurrentDictionary<EnvelopeId, byte> _inFlight = new();
    private readonly ConcurrentDictionary<EnvelopeId, byte> _processed = new();
    private readonly ConcurrentDictionary<EnvelopeId, DeadLetter> _deadLetters = new();

    /// <summary>
    /// Create an empty store.
    /// </summary>
    public InMemoryMessageStore()
    {
        Outbox = new OutboxStore(this);
        Inbox = new InboxStore(this);
        DeadLetter = new DeadLetterStore(this);
    }

    /// <summary>
    /// Create a store seeded with previously pending outbox envelopes, simulating recovery of a
    /// restarted host from the durable pending set.
    /// </summary>
    /// <param name="seedPending">Pending envelopes to recover.</param>
    public InMemoryMessageStore(IReadOnlyList<Envelope> seedPending)
        : this()
    {
        ArgumentNullException.ThrowIfNull(seedPending);

        foreach (Envelope envelope in seedPending)
        {
            _pending[envelope.Id] = envelope;
        }
    }

    /// <inheritdoc />
    public IOutboxStore Outbox { get; }

    /// <inheritdoc />
    public IInboxStore Inbox { get; }

    /// <inheritdoc />
    public IDeadLetterStore DeadLetter { get; }

    /// <summary>
    /// Begin a short-lived outbox transaction. Staging plus Commit/Rollback is the
    /// transactional middleware contract: one transaction per handler attempt, with a
    /// transaction-local staged buffer so concurrent attempts never clobber each other.
    /// </summary>
    /// <returns>A new single-use transaction that stages into this store.</returns>
    public IOutboxTransaction BeginOutboxTransaction() => new OutboxTransaction(this);

    private void Stage(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _staged[envelope.Id] = envelope;
    }

    private void CommitStaged()
    {
        foreach (KeyValuePair<EnvelopeId, Envelope> staged in _staged)
        {
            _pending[staged.Key] = staged.Value;
        }

        _staged.Clear();
    }

    private void RollbackStaged() => _staged.Clear();

    private sealed class OutboxStore : IOutboxStore
    {
        private readonly InMemoryMessageStore _store;

        public OutboxStore(InMemoryMessageStore store) => _store = store;

        public IOutboxTransaction BeginOutboxTransaction() => _store.BeginOutboxTransaction();

        public ValueTask StageAsync(Envelope envelope, CancellationToken ct = default)
        {
            _store.Stage(envelope);
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(CancellationToken ct = default)
        {
            _store.CommitStaged();
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken ct = default)
        {
            _store.RollbackStaged();
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<Envelope>> LoadPendingAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<Envelope>>(_store._pending.Values.ToList());

        public ValueTask MarkSentAsync(EnvelopeId id, CancellationToken ct = default)
        {
            _store._pending.TryRemove(id, out _);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class InboxStore : IInboxStore
    {
        private readonly InMemoryMessageStore _store;

        public InboxStore(InMemoryMessageStore store) => _store = store;

        public ValueTask<bool> TryAcquireAsync(EnvelopeId id, CancellationToken ct = default)
        {
            if (_store._processed.ContainsKey(id))
            {
                return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(_store._inFlight.TryAdd(id, 0));
        }

        public ValueTask MarkProcessedAsync(EnvelopeId id, CancellationToken ct = default)
        {
            _store._inFlight.TryRemove(id, out _);
            _store._processed[id] = 0;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(EnvelopeId id, CancellationToken ct = default)
        {
            _store._inFlight.TryRemove(id, out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<EnvelopeId>> LoadInFlightAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<EnvelopeId>>(_store._inFlight.Keys.ToList());
    }

    private sealed class DeadLetterStore : IDeadLetterStore
    {
        private readonly InMemoryMessageStore _store;

        public DeadLetterStore(InMemoryMessageStore store) => _store = store;

        public ValueTask RecordAsync(Envelope envelope, Exception cause, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            ArgumentNullException.ThrowIfNull(cause);
            _store._deadLetters[envelope.Id] = new DeadLetter(envelope, cause, DateTimeOffset.UtcNow);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<DeadLetter>> ListAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<DeadLetter>>(_store._deadLetters.Values.ToList());
    }

    private sealed class OutboxTransaction : IOutboxTransaction
    {
        private readonly InMemoryMessageStore _store;
        private readonly List<Envelope> _staged = [];
        private bool _completed;

        public OutboxTransaction(InMemoryMessageStore store) => _store = store;

        public ValueTask StageAsync(Envelope envelope, CancellationToken ct = default)
        {
            ThrowIfCompleted();
            ArgumentNullException.ThrowIfNull(envelope);
            _staged.Add(envelope);
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(CancellationToken ct = default)
        {
            ThrowIfCompleted();
            foreach (Envelope envelope in _staged)
            {
                _store._pending[envelope.Id] = envelope;
            }

            _staged.Clear();
            _completed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken ct = default)
        {
            ThrowIfCompleted();
            _staged.Clear();
            _completed = true;
            return ValueTask.CompletedTask;
        }

        private void ThrowIfCompleted()
        {
            if (_completed)
            {
                throw new OutboxTransactionAlreadyCompletedException();
            }
        }
    }
}
