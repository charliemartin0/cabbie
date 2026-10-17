using Cabbie.Application.Discovery;
using Cabbie.Application.Middleware;
using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Middleware;

/// <summary>
/// Prove-with for TransactionalOutboxMiddleware: it opens one transaction per attempt,
/// exposes it as the ambient scope during the handler, registers it under the envelope id
/// for the owning call site, rolls back on throw, and never stages or commits — staging
/// and committing are owned by the call sites (MessageDelivery / Mediator saga branch).
/// </summary>
public sealed class TransactionalOutboxMiddlewareTests
{
    [Fact]
    public async Task middleware_opens_a_transaction_and_sets_the_ambient_scope_during_next()
    {
        var store = new RecordingOutboxStore();
        var middleware = new TransactionalOutboxMiddleware(store);
        Envelope envelope = EnvelopeFactory.Create();
        IOutboxTransaction? seen = null;

        await middleware.InvokeAsync(
            envelope,
            HandlerFor<OutboxProbeHandler>(),
            () =>
            {
                seen = OutboxTransactionScope.Current;
                return Task.FromResult<object?>(null);
            },
            CancellationToken.None);

        Assert.Equal(1, store.BeginCount);
        Assert.Same(store.Transaction, seen);
        // The middleware leaves the transaction registered under the envelope id so the
        // owning call site — which runs after this middleware's async flow is gone — can
        // take it to stage and commit.
        Assert.Same(store.Transaction, OutboxTransactionScope.Take(envelope.Id));
        Assert.Null(OutboxTransactionScope.Take(envelope.Id));
    }

    [Fact]
    public async Task middleware_does_not_commit_or_dispatch_on_handler_success()
    {
        var store = new RecordingOutboxStore();
        var middleware = new TransactionalOutboxMiddleware(store);
        Envelope envelope = EnvelopeFactory.Create();
        object value = new OutboxProbeMessage(1);

        object? result = await middleware.InvokeAsync(
            envelope,
            HandlerFor<OutboxProbeHandler>(),
            () => Task.FromResult<object?>(value),
            CancellationToken.None);

        Assert.Same(value, result);
        Assert.Equal(0, store.Transaction.CommitCount);
        Assert.Equal(0, store.Transaction.RollbackCount);
        Assert.Empty(store.Transaction.Staged);
        Assert.Same(store.Transaction, OutboxTransactionScope.Take(envelope.Id));
    }

    [Fact]
    public async Task middleware_rolls_back_on_handler_throw()
    {
        var store = new RecordingOutboxStore();
        var middleware = new TransactionalOutboxMiddleware(store);
        Envelope envelope = EnvelopeFactory.Create();

        await Assert.ThrowsAsync<TimeoutException>(
            () => middleware.InvokeAsync(
                envelope,
                HandlerFor<OutboxProbeHandler>(),
                () => throw new TimeoutException("boom"),
                CancellationToken.None));

        Assert.Equal(1, store.Transaction.RollbackCount);
        Assert.Equal(0, store.Transaction.CommitCount);
        // A failed attempt leaves no registration for the call site to commit.
        Assert.Null(OutboxTransactionScope.Take(envelope.Id));
    }

    [Fact]
    public async Task middleware_does_not_double_rollback_if_handler_already_completed()
    {
        var store = new RecordingOutboxStore
        {
            Transaction = new RecordingOutboxTransaction
            {
                RollbackException = new OutboxTransactionAlreadyCompletedException(),
            },
        };
        var middleware = new TransactionalOutboxMiddleware(store);
        Envelope envelope = EnvelopeFactory.Create();

        await Assert.ThrowsAsync<TimeoutException>(
            () => middleware.InvokeAsync(
                envelope,
                HandlerFor<OutboxProbeHandler>(),
                () => throw new TimeoutException("boom"),
                CancellationToken.None));

        Assert.Equal(1, store.Transaction.RollbackCount);
        Assert.Null(OutboxTransactionScope.Take(envelope.Id));
    }

    [Fact]
    public async Task middleware_unregisters_and_ends_scope_even_when_rollback_throws_non_completed_exception()
    {
        var store = new RecordingOutboxStore
        {
            Transaction = new RecordingOutboxTransaction
            {
                RollbackException = new InvalidOperationException("rollback exploded"),
            },
        };
        var middleware = new TransactionalOutboxMiddleware(store);
        Envelope envelope = EnvelopeFactory.Create();

        // The handler throws; the rollback ALSO throws a real failure (not the
        // already-completed signal). The rollback failure propagates, but the cleanup
        // still runs in the finally: the registration is removed and the ambient scope
        // is restored, so the next handler attempt starts clean.
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(
                envelope,
                HandlerFor<OutboxProbeHandler>(),
                () => throw new TimeoutException("boom"),
                CancellationToken.None));

        Assert.Equal("rollback exploded", error.Message);
        Assert.Null(OutboxTransactionScope.Current);
        Assert.Null(OutboxTransactionScope.Take(envelope.Id));
    }

    [Fact]
    public async Task middleware_restores_ambient_scope_on_exception()
    {
        var prior = new RecordingOutboxTransaction();
        using IDisposable outer = OutboxTransactionScope.Begin(prior);
        var store = new RecordingOutboxStore();
        var middleware = new TransactionalOutboxMiddleware(store);

        await Assert.ThrowsAsync<TimeoutException>(
            () => middleware.InvokeAsync(
                EnvelopeFactory.Create(),
                HandlerFor<OutboxProbeHandler>(),
                () => throw new TimeoutException("boom"),
                CancellationToken.None));

        Assert.Same(prior, OutboxTransactionScope.Current);
    }

    private static DiscoveredHandler HandlerFor<THandler>()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(THandler));
        return Assert.Single(catalog.Handlers);
    }
}

public sealed record OutboxProbeMessage(int Id);

public sealed class OutboxProbeHandler
{
    public void Handle(OutboxProbeMessage message)
    {
    }
}

public sealed class RecordingOutboxStore : IOutboxStore
{
    public int BeginCount;

    public RecordingOutboxTransaction Transaction { get; set; } = new();

    public IOutboxTransaction BeginOutboxTransaction()
    {
        BeginCount++;
        return Transaction;
    }

    public ValueTask StageAsync(Envelope envelope, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask CommitAsync(CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask RollbackAsync(CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask<IReadOnlyList<Envelope>> LoadPendingAsync(CancellationToken ct = default) =>
        throw new NotImplementedException();

    public ValueTask MarkSentAsync(Cabbie.Domain.Envelope.ValueObjects.EnvelopeId id, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

public sealed class RecordingOutboxTransaction : IOutboxTransaction
{
    public List<Envelope> Staged { get; } = [];

    public int CommitCount { get; private set; }

    public int RollbackCount { get; private set; }

    public Exception? RollbackException { get; init; }

    public ValueTask StageAsync(Envelope envelope, CancellationToken ct = default)
    {
        Staged.Add(envelope);
        return ValueTask.CompletedTask;
    }

    public ValueTask CommitAsync(CancellationToken ct = default)
    {
        CommitCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask RollbackAsync(CancellationToken ct = default)
    {
        RollbackCount++;
        if (RollbackException is not null)
        {
            throw RollbackException;
        }

        return ValueTask.CompletedTask;
    }
}