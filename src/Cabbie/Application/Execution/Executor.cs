using System.Reflection;
using System.Runtime.ExceptionServices;
using Cabbie.Application.Bus;
using Cabbie.Application.Cascades;
using Cabbie.Application.Discovery;
using Cabbie.Application.Middleware;
using Cabbie.Application.Scheduling;
using Cabbie.Application.Tracking;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Errors.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cabbie.Application.Execution;

/// <summary>
/// Wrap one handler call: Attempts, typed error policy, missing handler.
/// Outer middleware around the retry loop; inner around each Handle. Fan-out is one wrap per handler.
/// HandleMissingAsync is not wrapped.
/// </summary>
public sealed class Executor
{
    private readonly ErrorPolicyCatalog _policies;
    private readonly IErrorQueue? _errorQueue;
    private readonly IMissingHandler? _missingHandler;
    private readonly MiddlewareCatalog _middleware;
    private readonly IScheduledEnvelopeHold? _scheduled;
    private readonly IHandlerAttemptObserver? _attempts;
    private readonly Func<ISender>? _sender;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeSpan? _handlerTimeout;
    private readonly ILogger<Executor> _logger;

    /// <summary>
    /// Used when no container is supplied: an empty provider, so handlers are built by
    /// ActivatorUtilities and only Envelope / CancellationToken slots resolve.
    /// </summary>
    private static readonly IServiceScopeFactory EmptyScopes =
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

    private static readonly TimeSpan MaxHandlerTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly TimeProvider _time;

    public Executor(
        ErrorPolicyCatalog policies,
        IErrorQueue? errorQueue = null,
        IMissingHandler? missingHandler = null,
        MiddlewareCatalog? middleware = null,
        IScheduledEnvelopeHold? scheduled = null,
        IHandlerAttemptObserver? attempts = null,
        Func<ISender>? sender = null,
        IServiceScopeFactory? scopes = null,
        TimeSpan? handlerTimeout = null,
        ILogger<Executor>? logger = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(policies);
        if (handlerTimeout is { } timeoutLimit)
        {
            ValidateHandlerTimeout(timeoutLimit, nameof(handlerTimeout));
        }

        _policies = policies;
        _errorQueue = errorQueue;
        _missingHandler = missingHandler;
        _middleware = middleware ?? new MiddlewareCatalog();
        _scheduled = scheduled;
        _attempts = attempts;
        _sender = sender;
        _scopes = scopes ?? EmptyScopes;
        _handlerTimeout = handlerTimeout;
        _logger = logger ?? NullLogger<Executor>.Instance;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// A handler timeout must be positive and no longer than <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> allows.
    /// </summary>
    internal static void ValidateHandlerTimeout(TimeSpan timeout, string paramName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, MaxHandlerTimeout, paramName);
    }

    public Task<object?> InvokeAsync(
        Envelope envelope,
        DiscoveredHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(handler);
        InvocationKind kind = handler.Scheduled ? InvocationKind.Scheduled : InvocationKind.Invoke;
        return InvokeCore(envelope, handler, kind, cancellationToken);
    }

    /// <summary>
    /// Like <see cref="InvokeAsync"/>, but reports whether the handler's failure was handed to a
    /// later attempt (a policy <c>Requeue</c> or <c>ScheduleRetry</c>). A handed-off attempt did not
    /// complete, so the inbox must not mark it processed.
    /// </summary>
    internal async Task<(object? Result, bool HandedOff)> InvokeTrackingHandOffAsync(
        Envelope envelope,
        DiscoveredHandler handler,
        CancellationToken cancellationToken,
        Func<CancellationToken, ValueTask>? beforeRequeue = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(handler);
        InvocationKind kind = handler.Scheduled ? InvocationKind.Scheduled : InvocationKind.Invoke;
        var handOff = new HandOffFlag { BeforeRequeue = beforeRequeue };
        object? result = await InvokeCore(envelope, handler, kind, cancellationToken, handOff);
        return (result, handOff.Value);
    }

    private sealed class HandOffFlag
    {
        internal bool Value { get; set; }

        /// <summary>
        /// Runs just before a requeued attempt (same envelope id) is sent, so the caller can free
        /// per-envelope state the next attempt will need.
        /// </summary>
        internal Func<CancellationToken, ValueTask>? BeforeRequeue { get; init; }
    }

    private async Task<object?> InvokeCore(
        Envelope envelope,
        DiscoveredHandler handler,
        InvocationKind kind,
        CancellationToken cancellationToken,
        HandOffFlag? handOff = null)
    {
        try
        {
            return await _middleware.InvokeAsync(
                MiddlewareLayer.Outer,
                envelope,
                handler,
                () => InvokeWithRetries(envelope, handler, cancellationToken, kind, handOff),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is not HandlerFault
            && exception is not MiddlewareNextViolation
            && exception is not OperationCanceledException
            && exception is not ScheduleRetryNotSupportedOnInvoke
            && exception is not NestedTrackNotSupported
            && exception is not TrackInProgress)
        {
            throw new HandlerFault(exception);
        }
    }

    private async Task<object?> InvokeWithRetries(
        Envelope envelope,
        DiscoveredHandler handler,
        CancellationToken cancellationToken,
        InvocationKind kind,
        HandOffFlag? handOff)
    {
        Envelope current = envelope;
        TimeSpan? timeout = handler.Timeout ?? _handlerTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using CancellationTokenSource? timeoutCts = timeout is { } budget
                ? new CancellationTokenSource(budget, _time)
                : null;
            using CancellationTokenSource? linkedCts = timeoutCts is null
                ? null
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            CancellationToken attemptToken = linkedCts?.Token ?? cancellationToken;
            using IDisposable? scope = _logger.BeginScope(AttemptScope(current));
            try
            {
                object? result = await _middleware.InvokeAsync(
                    MiddlewareLayer.Inner,
                    current,
                    handler,
                    () => InvokeOnce(current, handler, attemptToken),
                    attemptToken);
                _attempts?.OnAttempt(current, handler, exception: null);
                return result;
            }
            catch (Exception exception)
            {
                Exception fault = Unwrap(exception);
                if (fault is OperationCanceledException timedOut
                    && timeoutCts is { IsCancellationRequested: true }
                    && !cancellationToken.IsCancellationRequested)
                {
                    fault = new HandlerTimedOut(handler.HandlerType, timeout!.Value, timedOut);
                }

                // Only the caller's cancellation is a cancellation; any other OperationCanceledException
                // (an HTTP client timeout, say) is a fault and goes through the policy.
                if ((fault is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    || fault is MiddlewareNextViolation
                        or ScheduleRetryNotSupportedOnInvoke
                        or NestedTrackNotSupported
                        or TrackInProgress)
                {
                    ExceptionDispatchInfo.Capture(fault).Throw();
                    throw;
                }

                _attempts?.OnAttempt(current, handler, fault);
                if (fault is HandlerDependencyMissing)
                {
                    // A missing registration is a configuration error: another attempt cannot succeed.
                    throw new HandlerFault(fault);
                }

                Envelope? next = await NextAttempt(current, handler, fault, cancellationToken, kind, handOff);
                if (next is null)
                {
                    return null;
                }

                current = next;
            }
        }
    }

    public Task HandleMissingAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (_missingHandler is null)
        {
            throw new HandlerNotFound(envelope.Message.Value.GetType());
        }

        return _missingHandler.HandleAsync(envelope, cancellationToken);
    }

    private async Task<Envelope?> NextAttempt(
        Envelope current,
        DiscoveredHandler handler,
        Exception fault,
        CancellationToken cancellationToken,
        InvocationKind kind,
        HandOffFlag? handOff)
    {
        ErrorPolicyLookup lookup = _policies.For(
            fault.GetType(),
            handler.MessageClrType,
            useDefault: kind is not InvocationKind.Invoke);
        if (lookup is not FoundErrorPolicy found)
        {
            throw new HandlerFault(fault);
        }

        int index = current.Attempts.Value - 1;
        if (index < 0 || index >= found.Actions.Count)
        {
            throw new HandlerFault(fault);
        }

        switch (found.Actions[index])
        {
            case Retry:
                return WithNextAttempt(current);
            case RetryWithCooldown cooldown:
                if (cooldown.Delay > TimeSpan.Zero)
                {
                    await Task.Delay(cooldown.Delay, _time, cancellationToken);
                }

                return WithNextAttempt(current);
            case MoveToErrorQueue:
                if (_errorQueue is not null)
                {
                    try
                    {
                        await _errorQueue.MoveAsync(current, fault, cancellationToken);
                    }
                    catch (Exception writeFailure) when (
                        writeFailure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        ExecutorLog.DeadLetterWriteFailed(_logger, writeFailure, current.Id.Value, handler.HandlerType);
                        throw new HandlerFault(new DeadLetterWriteFailed(current.Id.Value, fault, writeFailure));
                    }

                    throw new HandlerFault(fault, deadLettered: true);
                }

                throw new HandlerFault(fault);
            case ScheduleRetry retry:
                if (kind is InvocationKind.Invoke)
                {
                    throw new ScheduleRetryNotSupportedOnInvoke();
                }

                await ParkScheduledRetryAsync(current, handler, retry, cancellationToken);
                MarkHandedOff(handOff);
                return null;
            case Requeue requeue:
                if (_sender is null)
                {
                    throw new HandlerFault(fault);
                }

                if (requeue.Delay > TimeSpan.Zero)
                {
                    await Task.Delay(requeue.Delay, _time, cancellationToken);
                }

                if (handOff?.BeforeRequeue is { } beforeRequeue)
                {
                    await beforeRequeue(CancellationToken.None);
                }

                await _sender().SendAsync(
                    WithNextAttempt(current) with { Handler = new HandlerName(handler.Name) },
                    cancellationToken);
                MarkHandedOff(handOff);
                return null;
            case Discard:
                return null;
            default:
                throw new HandlerFault(fault);
        }
    }

    private static void MarkHandedOff(HandOffFlag? handOff)
    {
        if (handOff is not null)
        {
            handOff.Value = true;
        }
    }

    private ValueTask ParkScheduledRetryAsync(
        Envelope current,
        DiscoveredHandler handler,
        ScheduleRetry retry,
        CancellationToken cancellationToken)
    {
        if (_scheduled is null)
        {
            throw new HandlerFault(new InvalidOperationException("ScheduleRetry requires a scheduled envelope hold."));
        }

        // Own id per handler retry: fan-out retries of one envelope must not collide in the hold.
        // CausationId keeps the link to the original envelope.
        Envelope parked = current with
        {
            Id = new EnvelopeId(Guid.NewGuid()),
            CausationId = new CausationId(current.Id.Value),
            Attempts = new Attempts(current.Attempts.Value + 1),
            DeliverBy = new DeliverBy(_time.GetUtcNow() + retry.Delay),
            Handler = new HandlerName(handler.Name)
        };
        return _scheduled.ParkAsync(parked, cancellationToken);
    }

    private static Dictionary<string, object> AttemptScope(Envelope envelope)
    {
        var scope = new Dictionary<string, object>
        {
            ["EnvelopeId"] = envelope.Id.Value,
            ["CorrelationId"] = envelope.CorrelationId.Value,
            ["ConversationId"] = envelope.ConversationId.Value,
        };
        if (envelope.CausationId is { } causation)
        {
            scope["CausationId"] = causation.Value;
        }

        return scope;
    }

    private static Envelope WithNextAttempt(Envelope current) =>
        current with { Attempts = new Attempts(current.Attempts.Value + 1) };

    /// <summary>
    /// One attempt: a fresh scope (disposed on success, fault and cancellation) from which the
    /// handler and its method services are resolved, so a retry never inherits state from the
    /// attempt before it.
    /// </summary>
    private async Task<object?> InvokeOnce(
        Envelope envelope,
        DiscoveredHandler handler,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        object? target = handler.IsStatic ? null : HandlerServices.CreateTarget(services, handler);
        object? result = handler.CachedInvoker is { } invoker
            ? invoker(target, envelope.Message.Value, envelope, cancellationToken, services)
            : InvokeByReflection(handler, target, envelope, services, cancellationToken);

        if (result is Task task)
        {
            await task;
        }
        else if (TaskResultAccessor.IsBoxedValueTask(result))
        {
            // ValueTask returns are not awaited by pattern (ValueTask<T> and ValueTask
            // are unrelated structs); convert non-consumingly and await the backing task
            // so the cascade unpack below sees a completed Task.
            Task asTask = TaskResultAccessor.AsTask(result!);
            await asTask;
            return asTask;
        }

        return result;
    }

    /// <summary>
    /// Fallback for handlers built at runtime without a compiled invoker (the saga
    /// NotFound miss path). Not the hot path.
    /// </summary>
    private static object? InvokeByReflection(
        DiscoveredHandler handler,
        object? target,
        Envelope envelope,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        try
        {
            return handler.Method.Invoke(target, BindArguments(envelope, handler, services, cancellationToken));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static object?[] BindArguments(
        Envelope envelope,
        DiscoveredHandler handler,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        object?[] arguments = new object?[1 + handler.InjectionSlots.Count];
        arguments[0] = envelope.Message.Value;
        for (int i = 0; i < handler.InjectionSlots.Count; i++)
        {
            ParameterInfo slot = handler.InjectionSlots[i];
            arguments[i + 1] = slot.ParameterType == typeof(Envelope) ? envelope
                : slot.ParameterType == typeof(CancellationToken) ? cancellationToken
                : HandlerServices.Resolve(services, slot);
        }

        return arguments;
    }

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
}
