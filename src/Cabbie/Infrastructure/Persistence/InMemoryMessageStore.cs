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
    private readonly ConcurrentDictionary<InboxEntry, byte> _inFlight = new();
    private readonly Dictionary<InboxEntry, DateTimeOffset> _processed = new();
    private readonly Queue<(InboxEntry Key, DateTimeOffset At)> _processedOrder = new();
    private readonly object _inboxGate = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _inboxRetention;
    private readonly int _inboxCapacity;
    private readonly ConcurrentDictionary<EnvelopeId, DeadLetter> _deadLetters = new();

    /// <summary>
    /// Default time a processed inbox entry is remembered for duplicate detection.
    /// </summary>
    public static readonly TimeSpan DefaultInboxRetention = TimeSpan.FromHours(1);

    /// <summary>
    /// Default maximum number of processed inbox entries kept.
    /// </summary>
    public const int DefaultInboxCapacity = 100_000;

    /// <summary>
    /// Create an empty store. Processed inbox entries are forgotten after
    /// <paramref name="inboxRetention"/> or once more than <paramref name="inboxCapacity"/> are held
    /// (oldest first), so the inbox cannot grow without bound.
    /// </summary>
    public InMemoryMessageStore(
        TimeProvider? timeProvider = null,
        TimeSpan? inboxRetention = null,
        int inboxCapacity = DefaultInboxCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(inboxCapacity, 0);
        if (inboxRetention is { } retention)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero, nameof(inboxRetention));
        }

        _time = timeProvider ?? TimeProvider.System;
        _inboxRetention = inboxRetention ?? DefaultInboxRetention;
        _inboxCapacity = inboxCapacity;
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

    private void PurgeProcessed()
    {
        DateTimeOffset cutoff = _time.GetUtcNow() - _inboxRetention;
        while (_processedOrder.Count > 0
            && (_processedOrder.Count > _inboxCapacity || _processedOrder.Peek().At <= cutoff))
        {
            _processed.Remove(_processedOrder.Dequeue().Key);
        }
    }

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

        public ValueTask<bool> TryAcquireAsync(EnvelopeId id, HandlerName handler, CancellationToken ct = default)
        {
            var key = new InboxEntry(id, handler);
            lock (_store._inboxGate)
            {
                _store.PurgeProcessed();
                return ValueTask.FromResult(!_store._processed.ContainsKey(key) && _store._inFlight.TryAdd(key, 0));
            }
        }

        public ValueTask MarkProcessedAsync(EnvelopeId id, HandlerName handler, CancellationToken ct = default)
        {
            var key = new InboxEntry(id, handler);
            lock (_store._inboxGate)
            {
                _store._inFlight.TryRemove(key, out _);
                DateTimeOffset now = _store._time.GetUtcNow();
                if (_store._processed.TryAdd(key, now))
                {
                    _store._processedOrder.Enqueue((key, now));
                }

                _store.PurgeProcessed();
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(EnvelopeId id, HandlerName handler, CancellationToken ct = default)
        {
            _store._inFlight.TryRemove(new InboxEntry(id, handler), out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<InboxEntry>> LoadInFlightAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<InboxEntry>>(_store._inFlight.Keys.ToList());
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
