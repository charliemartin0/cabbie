using Cabbie.Application.Bus;
using Cabbie.Application.Cascades;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Persistence;
using Cabbie.Application.Routing;
using Cabbie.Application.Sagas;
using Cabbie.Application.Scheduling;
using Cabbie.Application.Tracking;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.Validators;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Domain.Sagas;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Infrastructure.Sagas;
using Cabbie.Infrastructure.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace Cabbie.Application.Mediator;

public sealed class Mediator : IMessageBus
{
    private static readonly AsyncLocal<bool> InHandler = new();

    private static readonly Destination InvokeDestination = new(new Uri("local://invoke/"));

    private static readonly EnvelopeValidator EnvelopeValidator = new();

    private readonly Executor _executor;
    private readonly MessageDelivery _delivery;
    private readonly OutgoingDispatcher _dispatcher;
    private readonly ISagaStore _sagas;
    private readonly MessageScheduler _scheduler;
    private readonly RoutingCatalog _routing;
    private readonly ISender _sender;
    private readonly Queue<(object Body, Envelope Parent)> _worklist = [];
    private readonly object _trackGate = new();

    private SessionLog? _log;
    private bool _tracking;

    public HandlerCatalog Catalog { get; }

    public Mediator(
        HandlerCatalog catalog,
        ICascadePublisher? cascades = null,
        Executor? executor = null,
        IScheduledEnvelopeHold? hold = null,
        ISagaStore? sagas = null,
        ErrorPolicyCatalog? policies = null,
        RoutingCatalog? routing = null,
        ISender? sender = null,
        IHandlerAttemptObserver? attempts = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Catalog = catalog;
        _routing = routing ?? new RoutingCatalog();
        _sender = sender ?? NullSender.Instance;
        IScheduledEnvelopeHold inner = hold ?? new InMemoryScheduledEnvelopeHold();
        var scheduled = new RecordingScheduledEnvelopeHold(inner, OnPark);
        _dispatcher = new OutgoingDispatcher(scheduled, cascades);
        _executor = executor ?? new Executor(
            policies ?? new ErrorPolicyCatalog(),
            scheduled: scheduled,
            attempts: new SessionAttemptObserver(this),
            sender: () => _sender);
        if (attempts is AttemptObserverHub hub)
        {
            // The host shares one Executor; bind this session logger so tracked
            // sessions keep recording Executed bags without owning the Executor.
            hub.Bind(new SessionAttemptObserver(this));
        }

        _sagas = sagas ?? new InMemorySagaStore();
        _delivery = new MessageDelivery(
            catalog,
            _executor,
            hold: scheduled,
            routing: _routing,
            onImmediate: OnImmediateAsync,
            cascades: cascades);
        _scheduler = new MessageScheduler(
            scheduled,
            (envelope, cancellationToken) =>
                DispatchHandlers(envelope, scheduled: true, cancellationToken));
    }

    public Task InvokeAsync(object message, CancellationToken cancellationToken = default) =>
        InvokeAsync(message, new DeliveryOptions(), cancellationToken);

    public async Task InvokeAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellationToken = default)
    {
        Envelope envelope = EnvelopeForInvoke(message, options);
        await DispatchHandlers(envelope, scheduled: false, cancellationToken);
    }

    public Task<TResult> InvokeAsync<TResult>(object message, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task PublishAsync(object message, CancellationToken cancellationToken = default) =>
        PublishAsync(message, new DeliveryOptions(), cancellationToken);

    public Task PublishAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcher.TryPark(message, options))
        {
            return Task.CompletedTask;
        }

        return _sender.SendAsync(EnvelopeForPublish(message, _routing.For(message)), cancellationToken).AsTask();
    }

    public Task<TrackedSession> InvokeTrackedAsync(
        object message,
        CancellationToken cancellationToken = default) =>
        InvokeTrackedAsync(message, new DeliveryOptions(), cancellationToken);

    public async Task<TrackedSession> InvokeTrackedAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellationToken = default)
    {
        using IDisposable session = BeginTrack();
        try
        {
            Envelope envelope = EnvelopeForInvoke(message, options);
            await DispatchHandlers(envelope, scheduled: false, cancellationToken);
            await DrainAsync(cancellationToken);
            return Freeze();
        }
        catch (HandlerFault fault)
        {
            fault.Session = Freeze();
            throw;
        }
    }

    public Task<TrackedSession> PublishTrackedAsync(
        object message,
        CancellationToken cancellationToken = default) =>
        PublishTrackedAsync(message, new DeliveryOptions(), cancellationToken);

    public async Task<TrackedSession> PublishTrackedAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);
        using IDisposable session = BeginTrack();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_dispatcher.TryPark(message, options))
            {
                return Freeze();
            }

            _log!.Published.Add(new PublishedRecord(message));
            Envelope envelope = EnvelopeForPublish(message, _routing.For(message));
            await DispatchHandlers(envelope, scheduled: true, cancellationToken);
            await DrainAsync(cancellationToken);
            return Freeze();
        }
        catch (HandlerFault fault)
        {
            fault.Session = Freeze();
            throw;
        }
    }

    private IDisposable BeginTrack()
    {
        if (InHandler.Value)
        {
            throw new NestedTrackNotSupported();
        }

        lock (_trackGate)
        {
            if (_tracking)
            {
                throw new TrackInProgress();
            }

            _tracking = true;
            _log = new SessionLog();
            _worklist.Clear();
        }

        return new TrackScope(this);
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (_worklist.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (object body, Envelope parent) = _worklist.Dequeue();
            Envelope child = EnvelopeForDescendant(body, parent, _routing.For(body));
            await DispatchHandlers(child, scheduled: true, cancellationToken);
        }
    }

    /// <summary>
    /// Drain and tracked Publish use Scheduled kind and inherit conversation/correlation ids.
    /// Public InvokeAsync stays Invoke-kind and mints new ids. Internal so the local queues
    /// and transports can route saga handlers through the saga orchestration (load/save).
    /// </summary>
    internal async Task DispatchHandlers(Envelope envelope, bool scheduled, CancellationToken cancellationToken)
    {
        HandlerLookup lookup = Catalog.Lookup(envelope.Message.Value.GetType());
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

        List<HandlerFault>? faults = null;
        foreach (DiscoveredHandler handler in targets)
        {
            InHandler.Value = true;
            try
            {
                if (IsSagaHandler(handler))
                {
                    DiscoveredHandler target = scheduled ? handler with { Scheduled = true } : handler;

                    // Take the outbox transaction the middleware registered for this
                    // envelope id, after the handler attempt: an AsyncLocal set inside the
                    // middleware does not flow back here, so the registration is the
                    // hand-off. Re-establish it as the ambient in THIS context so the
                    // cascade dispatch below stages into it. Commit (or roll back) happens
                    // in the finally AFTER _sagas.Save and the immediate-publish attempt —
                    // a Save throw or a failed dispatch must roll back, never leave the
                    // outbox pending ("no save then publish").
                    IOutboxTransaction? tx = null;
                    bool failed = true;
                    try
                    {
                        (Saga? instance, object? sagaResult) = await InvokeSagaAsync(envelope, target, cancellationToken);
                        tx = OutboxTransactionScope.Take(envelope.Id);
                        if (tx is not null)
                        {
                            OutboxTransactionScope.Begin(tx);
                        }

                        if (instance is not null)
                        {
                            SagaId sagaId = SagaIdentityNaming.For(envelope.Message.Value, target.HandlerType);
                            _sagas.Save(target.HandlerType, sagaId, instance);
                        }

                        await _delivery.DispatchOutgoing(CascadingMessages.From(sagaResult), envelope, cancellationToken);
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
                else
                {
                    await _delivery.InvokeAndDispatch(envelope, handler, scheduled, cancellationToken);
                }
            }
            catch (HandlerFault fault)
            {
                (faults ??= []).Add(fault);
            }
            finally
            {
                InHandler.Value = false;
            }
        }

        FanOut.Rethrow(faults);
    }

    /// <summary>
    /// Run the saga's handler through the executor and return the saga instance to persist
    /// (or <see langword="null"/> for the NotFound miss path) plus the handler's result.
    /// Persisting happens at the call site — after the handler attempt and before the
    /// cascade dispatch — so a save failure rolls back the outbox transaction instead of
    /// leaving it pending.
    /// </summary>
    private async Task<(Saga? Instance, object? Result)> InvokeSagaAsync(
        Envelope envelope,
        DiscoveredHandler handler,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        object message = envelope.Message.Value;
        Type sagaType = handler.HandlerType;
        SagaId sagaId = SagaIdentityNaming.For(message, sagaType);
        if (string.IsNullOrEmpty(sagaId.Value))
        {
            throw new SagaIdRequired(sagaType, message.GetType());
        }

        Envelope sagaEnvelope = envelope with { SagaId = sagaId };
        Saga? row = _sagas.Load(sagaType, sagaId);

        if (HandlerConvention.IsStart(handler.Method.Name))
        {
            if (row is not null)
            {
                throw new SagaAlreadyExists(sagaType, sagaId);
            }

            object? started = null;
            object? result = await _executor.InvokeAsync(
                sagaEnvelope,
                handler with { ResolveTarget = _ => started = Activator.CreateInstance(sagaType) },
                cancellationToken);
            return ((Saga)started!, result);
        }

        if (row is null || row.IsCompleted)
        {
            object? notFoundResult = await InvokeNotFoundAsync(sagaEnvelope, handler, sagaId, cancellationToken);
            return (null, notFoundResult);
        }

        object? loaded = null;
        object? handled = await _executor.InvokeAsync(
            sagaEnvelope,
            handler with { ResolveTarget = _ => loaded = _sagas.Load(sagaType, sagaId) },
            cancellationToken);
        return ((Saga)loaded!, handled);
    }

    private async Task<object?> InvokeNotFoundAsync(
        Envelope envelope,
        DiscoveredHandler handler,
        SagaId sagaId,
        CancellationToken cancellationToken)
    {
        DiscoveredHandler? notFound = NotFoundConvention.For(handler.HandlerType, handler.MessageClrType);
        if (notFound is null)
        {
            throw new SagaInstanceNotFound(handler.HandlerType, sagaId, handler.MessageClrType);
        }

        return await _executor.InvokeAsync(
            envelope,
            notFound with { ResolveTarget = _ => Activator.CreateInstance(handler.HandlerType) },
            cancellationToken);
    }

    private static bool IsSagaHandler(DiscoveredHandler handler) =>
        handler.HandlerType != typeof(Saga) && handler.HandlerType.IsAssignableTo(typeof(Saga));

    private Envelope EnvelopeForInvoke(object message, DeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Delay is not null && options.Until is not null)
        {
            throw new AmbiguousDeliveryOptions();
        }

        if (options.HasSchedule)
        {
            throw new DelayedInvokeNotSupported();
        }

        return EnvelopeForPublish(message, InvokeDestination);
    }

    private static Envelope EnvelopeForPublish(object message, Destination destination)
    {
        DateTimeOffset sent = DateTimeOffset.UtcNow;
        Envelope envelope = new(
            new EnvelopeId(Guid.NewGuid()),
            new Message(message),
            MessageTypeNaming.For(message.GetType()),
            destination,
            new CorrelationId(Guid.NewGuid()),
            new ConversationId(Guid.NewGuid()),
            new SagaId(""),
            new SentAt(sent),
            new DeliverBy(null),
            new Headers(),
            new ContentType(""),
            new Attempts(1),
            new EnvelopeData());

        FluentValidation.Results.ValidationResult validation = EnvelopeValidator.Validate(envelope);
        if (!validation.IsValid)
        {
            throw new FluentValidation.ValidationException(validation.Errors);
        }

        return envelope;
    }

    private static Envelope EnvelopeForDescendant(object message, Envelope parent, Destination destination)
    {
        DateTimeOffset sent = DateTimeOffset.UtcNow;
        return new Envelope(
            new EnvelopeId(Guid.NewGuid()),
            new Message(message),
            MessageTypeNaming.For(message.GetType()),
            destination,
            parent.CorrelationId,
            parent.ConversationId,
            parent.SagaId,
            new SentAt(sent),
            new DeliverBy(null),
            new Headers(),
            new ContentType(""),
            new Attempts(1),
            new EnvelopeData(),
            new CausationId(parent.Id.Value));
    }

    private void OnPark(Envelope envelope)
    {
        _log?.Scheduled.Add(new ScheduledRecord(envelope));
    }

    private async ValueTask OnImmediateAsync(object message, Envelope parent, CancellationToken cancellationToken)
    {
        if (_log is null)
        {
            await _sender.SendAsync(EnvelopeForDescendant(message, parent, _routing.For(message)), cancellationToken);
            return;
        }

        _log.Published.Add(new PublishedRecord(message));
        _worklist.Enqueue((message, parent));
    }

    private TrackedSession Freeze()
    {
        SessionLog log = _log ?? new SessionLog();
        return new TrackedSession(
            [.. log.Executed],
            [.. log.Published],
            [.. log.Scheduled],
            PlayScheduledMessagesAsync);
    }

    private async Task<TrackedSession> PlayScheduledMessagesAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        using IDisposable session = BeginTrack();
        try
        {
            await _scheduler.PlayDue(asOf, cancellationToken);
            await DrainAsync(cancellationToken);
            return Freeze();
        }
        catch (HandlerFault fault)
        {
            fault.Session = Freeze();
            throw;
        }
    }

    private void EndTrack()
    {
        lock (_trackGate)
        {
            _tracking = false;
            _log = null;
            _worklist.Clear();
        }
    }

    private sealed class SessionLog
    {
        public List<ExecutedRecord> Executed { get; } = [];

        public List<PublishedRecord> Published { get; } = [];

        public List<ScheduledRecord> Scheduled { get; } = [];
    }

    private sealed class SessionAttemptObserver : IHandlerAttemptObserver
    {
        private readonly Mediator _mediator;

        public SessionAttemptObserver(Mediator mediator) => _mediator = mediator;

        public void OnAttempt(Envelope envelope, DiscoveredHandler handler, Exception? exception)
        {
            _mediator._log?.Executed.Add(
                new ExecutedRecord(envelope.Message.Value, envelope, envelope.Attempts.Value, exception));
        }
    }

    private sealed class TrackScope : IDisposable
    {
        private readonly Mediator _mediator;

        public TrackScope(Mediator mediator) => _mediator = mediator;

        public void Dispose() => _mediator.EndTrack();
    }

    /// <summary>
    /// Default when no transport is configured: direct construction in tests still compiles
    /// and PublishAsync does not throw. The composition root always supplies the transport router.
    /// </summary>
    private sealed class NullSender : ISender
    {
        public static readonly NullSender Instance = new();

        public ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
