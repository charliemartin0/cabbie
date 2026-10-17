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
/// scheduled hold, immediate ones go to the owning transport (local:// today). The
/// Mediator binds its cascade/tracking hooks here; the local queues and transports use
/// the transport fallback. The <see cref="ITransport"/> factory is lazy so Hosting can
/// register the transport that itself needs this service.
/// </summary>
public sealed class MessageDelivery
{
    private static readonly Uri ScheduledDestination = new("local://scheduled/");

    private readonly HandlerCatalog _catalog;
    private readonly Executor _executor;
    private readonly IScheduledEnvelopeHold _hold;
    private readonly RoutingCatalog _routing;
    private readonly Func<ITransport>? _transport;
    private readonly Action<object, Envelope>? _onImmediate;
    private readonly ICascadePublisher? _cascades;

    public MessageDelivery(
        HandlerCatalog catalog,
        Executor executor,
        IScheduledEnvelopeHold? hold = null,
        RoutingCatalog? routing = null,
        Func<ITransport>? transport = null,
        Action<object, Envelope>? onImmediate = null,
        ICascadePublisher? cascades = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(executor);
        _catalog = catalog;
        _executor = executor;
        _hold = hold ?? new InMemoryScheduledEnvelopeHold();
        _routing = routing ?? new RoutingCatalog();
        _transport = transport;
        _onImmediate = onImmediate;
        _cascades = cascades;
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

        foreach (DiscoveredHandler handler in ((FoundHandlers)lookup).Handlers)
        {
            await InvokeAndDispatch(envelope, handler, scheduled, cancellationToken);
        }
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(handler);
        DiscoveredHandler target = scheduled ? handler with { Scheduled = true } : handler;

        // Take the outbox transaction the middleware registered for this envelope id,
        // after the handler attempt: an AsyncLocal set inside the middleware does not
        // flow back here, so the registration is the hand-off. Re-establish it as the
        // ambient in THIS context (which survives our own awaits) so DispatchOutgoing
        // stages into it. The finally uses this snapshot, not a fresh read of Current —
        // a nested handler or retry may have set/cleared the ambient since.
        IOutboxTransaction? tx = null;
        bool failed = true;
        try
        {
            object? result = await _executor.InvokeAsync(envelope, target, cancellationToken);
            tx = OutboxTransactionScope.Take(envelope.Id);
            if (tx is not null)
            {
                OutboxTransactionScope.Begin(tx);
            }

            IReadOnlyList<object> outgoing = CascadingMessages.From(result);
            if (outgoing.Count > 0)
            {
                await DispatchOutgoing(outgoing, envelope, cancellationToken);
            }

            failed = false;
        }
        finally
        {
            if (tx is not null)
            {
                await OutboxTransactionScope.Complete(tx, success: !failed, cancellationToken);
            }
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
                _onImmediate(message, parent);
            }
        }
        else if (_transport is not null)
        {
            ITransport transport = _transport();
            foreach (object message in immediate)
            {
                await transport.SendAsync(Build(message, DateTimeOffset.UtcNow, null, parent), ct);
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
            new EnvelopeData());
    }
}