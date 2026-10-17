using System.Reflection;
using System.Runtime.ExceptionServices;
using Cabbie.Application.Bus;
using Cabbie.Application.Cascades;
using Cabbie.Application.Discovery;
using Cabbie.Application.Middleware;
using Cabbie.Application.Scheduling;
using Cabbie.Application.Tracking;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Errors.ValueObjects;

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
    private readonly Func<IPublishEnqueuer>? _enqueuer;

    public Executor(
        ErrorPolicyCatalog policies,
        IErrorQueue? errorQueue = null,
        IMissingHandler? missingHandler = null,
        MiddlewareCatalog? middleware = null,
        IScheduledEnvelopeHold? scheduled = null,
        IHandlerAttemptObserver? attempts = null,
        Func<IPublishEnqueuer>? enqueuer = null)
    {
        ArgumentNullException.ThrowIfNull(policies);
        _policies = policies;
        _errorQueue = errorQueue;
        _missingHandler = missingHandler;
        _middleware = middleware ?? new MiddlewareCatalog();
        _scheduled = scheduled;
        _attempts = attempts;
        _enqueuer = enqueuer;
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

    private async Task<object?> InvokeCore(
        Envelope envelope,
        DiscoveredHandler handler,
        InvocationKind kind,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _middleware.InvokeAsync(
                MiddlewareLayer.Outer,
                envelope,
                handler,
                () => InvokeWithRetries(envelope, handler, cancellationToken, kind),
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
        InvocationKind kind)
    {
        Envelope current = envelope;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                object? result = await _middleware.InvokeAsync(
                    MiddlewareLayer.Inner,
                    current,
                    handler,
                    () => InvokeOnce(current, handler, cancellationToken),
                    cancellationToken);
                _attempts?.OnAttempt(current, handler, exception: null);
                return result;
            }
            catch (Exception exception)
            {
                Exception fault = Unwrap(exception);
                if (fault is OperationCanceledException
                    or MiddlewareNextViolation
                    or ScheduleRetryNotSupportedOnInvoke
                    or NestedTrackNotSupported
                    or TrackInProgress)
                {
                    ExceptionDispatchInfo.Capture(fault).Throw();
                    throw;
                }

                _attempts?.OnAttempt(current, handler, fault);
                Envelope? next = await NextAttempt(current, handler, fault, cancellationToken, kind);
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
        InvocationKind kind)
    {
        ErrorPolicyLookup lookup = _policies.For(fault.GetType(), handler.MessageClrType);
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
                    await Task.Delay(cooldown.Delay, cancellationToken);
                }

                return WithNextAttempt(current);
            case MoveToErrorQueue:
                _errorQueue?.Move(current, fault);
                throw new HandlerFault(fault);
            case ScheduleRetry retry:
                if (kind is InvocationKind.Invoke)
                {
                    throw new ScheduleRetryNotSupportedOnInvoke();
                }

                ParkScheduledRetry(current, retry);
                return null;
            case Requeue:
                if (_enqueuer is null)
                {
                    throw new HandlerFault(fault);
                }

                _enqueuer().Enqueue(current);
                return null;
            case Discard:
                return null;
            default:
                throw new HandlerFault(fault);
        }
    }

    private void ParkScheduledRetry(Envelope current, ScheduleRetry retry)
    {
        if (_scheduled is null)
        {
            throw new HandlerFault(new InvalidOperationException("ScheduleRetry requires a scheduled envelope hold."));
        }

        Envelope parked = current with
        {
            Attempts = new Attempts(current.Attempts.Value + 1),
            DeliverBy = new DeliverBy(DateTimeOffset.UtcNow + retry.Delay)
        };
        _scheduled.Park(parked);
    }

    private static Envelope WithNextAttempt(Envelope current) =>
        current with { Attempts = new Attempts(current.Attempts.Value + 1) };

    private static async Task<object?> InvokeOnce(
        Envelope envelope,
        DiscoveredHandler handler,
        CancellationToken cancellationToken)
    {
        object? result = handler.CachedInvoker is { } invoker
            ? invoker(ResolveTarget(handler), envelope.Message.Value, envelope, cancellationToken)
            : InvokeByReflection(handler, envelope, cancellationToken);

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

    private static object? ResolveTarget(DiscoveredHandler handler) =>
        handler.IsStatic ? null : handler.ResolveTarget?.Invoke();

    /// <summary>
    /// Fallback for handlers built at runtime without a compiled invoker (the saga
    /// NotFound miss path). Not the hot path.
    /// </summary>
    private static object? InvokeByReflection(
        DiscoveredHandler handler,
        Envelope envelope,
        CancellationToken cancellationToken)
    {
        object? target = handler.IsStatic
            ? null
            : handler.ResolveTarget is { } resolve
                ? resolve()
                : Activator.CreateInstance(handler.HandlerType);
        try
        {
            return handler.Method.Invoke(target, BindArguments(envelope, handler, cancellationToken));
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
        CancellationToken cancellationToken)
    {
        object?[] arguments = new object?[1 + handler.InjectionSlots.Count];
        arguments[0] = envelope.Message.Value;
        for (int i = 0; i < handler.InjectionSlots.Count; i++)
        {
            Type parameterType = handler.InjectionSlots[i].ParameterType;
            if (parameterType == typeof(Envelope))
            {
                arguments[i + 1] = envelope;
            }
            else if (parameterType == typeof(CancellationToken))
            {
                arguments[i + 1] = cancellationToken;
            }
        }

        return arguments;
    }

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
}
