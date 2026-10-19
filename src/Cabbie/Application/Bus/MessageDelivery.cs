using System.Collections.Concurrent;
using Cabbie.Application.Cascades;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Persistence;
using Cabbie.Application.Routing;
using Cabbie.Application.Scheduling;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Infrastructure.Scheduling;

namespace Cabbie.Application.Bus;

/// <summary>
/// One shared dispatch path for every envelope: look up the handler, run it through the
/// Executor, then enqueue the handler's return values — delayed messages park in the
/// scheduled hold, immediate ones go to the <see cref="ISender"/> (the transport router
/// when hosted, which picks the transport by the destination's URI scheme). The
/// Mediator binds its cascade/tracking hooks here; the local queues and transports use
/// the sender fallback.
/// </summary>
public sealed class MessageDelivery
{
    private static readonly Uri ScheduledDestination = new("local://scheduled/");

    private readonly HandlerCatalog _catalog;
    private readonly Executor _executor;
    private readonly IScheduledEnvelopeHold _hold;
    private readonly RoutingCatalog _routing;
    private readonly ISender? _sender;
    private readonly Func<object, Envelope, CancellationToken, ValueTask>? _onImmediate;
    private readonly ICascadePublisher? _cascades;
    private readonly IInboxStore? _inbox;
    private readonly ConcurrentDictionary<InboxEntry, IReadOnlyList<object>> _undeliveredCascades = new();

    public MessageDelivery(
        HandlerCatalog catalog,
        Executor executor,
        IScheduledEnvelopeHold? hold = null,
        RoutingCatalog? routing = null,
        ISender? sender = null,
        Func<object, Envelope, CancellationToken, ValueTask>? onImmediate = null,
        ICascadePublisher? cascades = null,
        IInboxStore? inbox = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(executor);
        _catalog = catalog;
        _executor = executor;
        _hold = hold ?? new InMemoryScheduledEnvelopeHold();
        _routing = routing ?? new RoutingCatalog();
        _sender = sender;
        _onImmediate = onImmediate;
        _cascades = cascades;
        _inbox = inbox;
    }

    /// <summary>
    /// Run every handler for the envelope's message type and enqueue the cascades.
    /// Scheduled callers (queues, transports, the scheduler) mark handlers Scheduled.
    /// </summary>
    public async Task Dispatch(
        Envelope envelope,
        bool scheduled = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        HandlerLookup lookup = _catalog.Lookup(envelope.Message.Value.GetType());
        if (lookup is MissingHandler)
        {
            await _executor.HandleMissingAsync(envelope, cancellationToken);
            return;
        }

        IReadOnlyList<DiscoveredHandler> targets = FanOut.Select(((FoundHandlers)lookup).Handlers, envelope);
        if (targets.Count == 0)
        {
            await _executor.HandleMissingAsync(envelope, cancellationToken);
            return;
        }

        await FanOut.RunAsync(
            targets,
            envelope,
            _executor,
            handler => InvokeAndDispatch(envelope, handler, scheduled, useInbox: true, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Run one handler and enqueue its return value. The Mediator uses this for the
    /// non-saga branch of its own dispatch loop (saga state machines stay in the Mediator).
    /// When the handler ran inside an ambient <see cref="IOutboxTransaction"/> (set by the
    /// transactional outbox middleware), the cascades are staged on it by
    /// <see cref="DispatchOutgoing"/> and this method commits the transaction in a
    /// <c>finally</c> — AFTER the immediate-publish attempt — so a throwing handler or a
    /// failed dispatch rolls back and publishes nothing.
    /// </summary>
    public async Task InvokeAndDispatch(
        Envelope envelope,
        DiscoveredHandler handler,
        bool scheduled = false,
        bool useInbox = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(handler);
        DiscoveredHandler target = scheduled ? handler with { Scheduled = true } : handler;

        if (!useInbox || _inbox is null)
        {
            await RunHandler(envelope, target, inboxKey: null, cancellationToken);
            return;
        }

        var key = new InboxEntry(envelope.Id, new HandlerName(handler.Name));
        if (!await _inbox.TryAcquireAsync(key.Id, key.Handler, cancellationToken))
        {
            // Already processed (or in flight elsewhere): never re-run the handler. If its cascades
            // failed to send last time, retry only those.
            await RetryUndeliveredCascades(key, envelope, cancellationToken);
            return;
        }

        await RunHandler(envelope, target, key, cancellationToken);
    }

    private async Task RunHandler(
        Envelope envelope,
        DiscoveredHandler target,
        InboxEntry? inboxKey,
        CancellationToken cancellationToken)
    {
        // Take the outbox transaction the middleware registered for this envelope id,
        // after the handler attempt: an AsyncLocal set inside the middleware does not
        // flow back here, so the registration is the hand-off. Re-establish it as the
        // ambient in THIS context (which survives our own awaits) so DispatchOutgoing
        // stages into it. The finally uses this snapshot, not a fresh read of Current —
        // a nested handler or retry may have set/cleared the ambient since.
        IOutboxTransaction? tx = null;
        bool failed = true;
        bool handedOff = false;
        bool released = false;
        bool markedProcessed = false;

        // A requeued attempt keeps this envelope id, so its inbox row must be free before the
        // send: a fast consumer would otherwise fail TryAcquire and ack the attempt away.
        async ValueTask ReleaseBeforeRequeue(CancellationToken ct)
        {
            released = true;
            await _inbox!.ReleaseAsync(inboxKey!.Id, inboxKey.Handler, ct);
        }

        try
        {
            object? result;
            if (inboxKey is null)
            {
                result = await _executor.InvokeAsync(envelope, target, cancellationToken);
            }
            else
            {
                (result, handedOff) = await _executor.InvokeTrackingHandOffAsync(
                    envelope, target, cancellationToken, ReleaseBeforeRequeue);
            }

            tx = OutboxTransactionScope.Take(envelope.Id);
            if (tx is not null)
            {
                OutboxTransactionScope.Begin(tx);
            }

            if (inboxKey is not null && !handedOff && tx is null)
            {
                // No outbox transaction: record the finished handler before the cascades go out,
                // so a cascade send failure never re-runs it.
                await _inbox!.MarkProcessedAsync(inboxKey.Id, inboxKey.Handler, cancellationToken);
                markedProcessed = true;
            }

            IReadOnlyList<object> outgoing = CascadingMessages.From(result);
            if (outgoing.Count > 0)
            {
                if (markedProcessed)
                {
                    _undeliveredCascades[inboxKey!] = outgoing;
                }

                await DispatchOutgoing(outgoing, envelope, cancellationToken);
            }

            failed = false;
        }
        finally
        {
            try
            {
                if (tx is not null)
                {
                    await OutboxTransactionScope.Complete(tx, success: !failed, cancellationToken);
                    if (!failed && inboxKey is not null && !handedOff)
                    {
                        // Only a committed outbox transaction counts as processed: a failed commit
                        // rolls the handler's work back, so redelivery must run it again.
                        await _inbox!.MarkProcessedAsync(inboxKey.Id, inboxKey.Handler, CancellationToken.None);
                        markedProcessed = true;
                    }
                }

                if (!failed && markedProcessed)
                {
                    // Sent (or staged and committed): nothing left to retry.
                    _undeliveredCascades.TryRemove(inboxKey!, out _);
                }
            }
            finally
            {
                if (inboxKey is not null && !markedProcessed && !released)
                {
                    // Faulted, cancelled or handed to a scheduled retry: let that attempt acquire again.
                    await _inbox!.ReleaseAsync(inboxKey.Id, inboxKey.Handler, CancellationToken.None);
                }
            }
        }
    }

    private async Task RetryUndeliveredCascades(InboxEntry key, Envelope envelope, CancellationToken cancellationToken)
    {
        if (!_undeliveredCascades.TryRemove(key, out IReadOnlyList<object>? outgoing))
        {
            return;
        }

        try
        {
            await DispatchOutgoing(outgoing, envelope, cancellationToken);
        }
        catch
        {
            _undeliveredCascades[key] = outgoing;
            throw;
        }
    }

    /// <summary>
    /// Enqueue a handler's return values after success: park delayed messages in the
    /// scheduled hold, send immediate ones through the bound hook or the owning transport.
    /// When a handler ran inside an ambient <see cref="IOutboxTransaction"/> (set by the
    /// transactional outbox middleware), the immediate cascades are staged on that
    /// transaction instead of being published — the owning call site commits them as
    /// pending after this method returns, so a throwing handler or a failed saga save
    /// rolls back and publishes nothing.
    /// </summary>
    public async Task DispatchOutgoing(
        IReadOnlyList<object> outgoing,
        Envelope parent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(outgoing);
        ArgumentNullException.ThrowIfNull(parent);
        var immediate = new List<object>();
        foreach (object item in outgoing)
        {
            (object message, DeliveryOptions? options) = UnwrapSchedule(item);
            DateTimeOffset sentAt = DateTimeOffset.UtcNow;
            DateTimeOffset? due = DelayedDelivery.DueAt(message.GetType(), options, sentAt);
            if (due is not null)
            {
                _hold.Park(Build(message, sentAt, due, parent));
                continue;
            }

            immediate.Add(message);
        }

        if (immediate.Count == 0)
        {
            return;
        }

        // Outbox redirect: an ambient transaction means the cascades must be staged, not
        // published. The owning call site commits/rolls back with the handler outcome;
        // staged envelopes become pending on commit and replay on host start.
        IOutboxTransaction? tx = OutboxTransactionScope.Current;
        if (tx is not null)
        {
            foreach (object message in immediate)
            {
                await tx.StageAsync(Build(message, DateTimeOffset.UtcNow, null, parent), ct).ConfigureAwait(false);
            }

            return;
        }

        if (_onImmediate is not null)
        {
            foreach (object message in immediate)
            {
                await _onImmediate(message, parent, ct);
            }
        }
        else if (_sender is not null)
        {
            foreach (object message in immediate)
            {
                await _sender.SendAsync(Build(message, DateTimeOffset.UtcNow, null, parent), ct);
            }
        }

        _cascades?.Publish(immediate);
    }

    private static (object Message, DeliveryOptions? Options) UnwrapSchedule(object item) =>
        item is ScheduledCascade scheduled
            ? (scheduled.Message, new DeliveryOptions { Until = scheduled.Until })
            : (item, null);

    private Envelope Build(object message, DateTimeOffset sentAt, DateTimeOffset? deliverBy, Envelope parent)
    {
        return new Envelope(
            new EnvelopeId(Guid.NewGuid()),
            new Message(message),
            MessageTypeNaming.For(message.GetType()),
            new Destination(deliverBy is null ? _routing.For(message).Value : ScheduledDestination),
            parent.CorrelationId,
            parent.ConversationId,
            parent.SagaId,
            new SentAt(sentAt),
            new DeliverBy(deliverBy),
            new Headers(),
            new ContentType(""),
            new Attempts(1),
            new EnvelopeData(),
            new CausationId(parent.Id.Value));
    }
}