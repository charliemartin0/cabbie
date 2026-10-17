using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Tests.Application.Execution;

/// <summary>
/// Handler timeouts: cooperative cancellation of the attempt token, surfaced as HandlerTimedOut
/// (a TimeoutException) so error policies can match it. Caller cancellation is never a timeout.
/// Time is a <see cref="ManualTimeProvider"/>: a timeout fires only when the test advances the clock.
/// </summary>
public sealed class ExecutorTimeoutTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    public ExecutorTimeoutTests()
    {
        TimeoutProbe.Reset();
    }

    [Fact]
    public async Task timeout_cancels_handler_token_and_is_retried_per_policy()
    {
        var clock = new ManualTimeProvider();
        var policies = new ErrorPolicyCatalog();
        policies.OnException<HandlerTimedOut>().Retry();
        var executor = new Executor(policies, timeProvider: clock);

        Task<object?> run = await StartAsync(executor, HandlerFor<BlocksOnFirstAttemptHandler>());
        clock.Advance(Short);
        await run;

        Assert.Equal([1, 2], TimeoutProbe.Attempts);
        Assert.Equal([true, false], TimeoutProbe.CancelledOnAttempt);
    }

    [Fact]
    public async Task timeout_matches_timeout_exception_policy()
    {
        var clock = new ManualTimeProvider();
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Discard();
        var executor = new Executor(policies, timeProvider: clock);

        Task<object?> run = await StartAsync(executor, HandlerFor<AlwaysBlocksHandler>());
        clock.Advance(Short);

        Assert.Null(await run);
        Assert.Equal([1], TimeoutProbe.Attempts);
    }

    [Fact]
    public async Task unhandled_timeout_surfaces_as_handler_fault_carrying_handler_timed_out()
    {
        var clock = new ManualTimeProvider();
        var executor = new Executor(new ErrorPolicyCatalog(), timeProvider: clock);

        Task<object?> run = await StartAsync(executor, HandlerFor<AlwaysBlocksHandler>());
        clock.Advance(Short);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(() => run);
        HandlerTimedOut timedOut = Assert.IsType<HandlerTimedOut>(fault.InnerException);
        Assert.Equal(typeof(AlwaysBlocksHandler), timedOut.HandlerType);
        Assert.Equal(Short, timedOut.Timeout);
        Assert.IsAssignableFrom<OperationCanceledException>(timedOut.InnerException);
    }

    [Fact]
    public async Task caller_cancellation_is_not_a_timeout()
    {
        var clock = new ManualTimeProvider();
        var policies = new ErrorPolicyCatalog();
        policies.OnException<Exception>().Retry().Retry();
        var executor = new Executor(policies, handlerTimeout: TimeSpan.FromSeconds(30), timeProvider: clock);
        using var cts = new CancellationTokenSource();

        Task<object?> run = await StartAsync(executor, HandlerFor<UnboundedBlockHandler>(), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal([1], TimeoutProbe.Attempts);
    }

    [Fact]
    public async Task handler_attribute_overrides_global_timeout()
    {
        var clock = new ManualTimeProvider();
        var executor = new Executor(new ErrorPolicyCatalog(), handlerTimeout: Short, timeProvider: clock);

        // The global timeout elapses, but the handler's own (longer) timeout wins: its token stays live.
        Task<object?> run = await StartAsync(executor, HandlerFor<SlowButAllowedHandler>());
        clock.Advance(Short);
        TimeoutProbe.ReleaseGate();
        await run;

        Assert.Equal([false], TimeoutProbe.CancelledOnAttempt);
    }

    [Fact]
    public async Task global_timeout_applies_to_handlers_without_an_attribute()
    {
        var clock = new ManualTimeProvider();
        var executor = new Executor(new ErrorPolicyCatalog(), handlerTimeout: Short, timeProvider: clock);

        Task<object?> run = await StartAsync(executor, HandlerFor<UnboundedBlockHandler>());
        clock.Advance(Short);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(() => run);
        Assert.IsType<HandlerTimedOut>(fault.InnerException);
    }

    [Fact]
    public async Task method_attribute_overrides_class_attribute()
    {
        var clock = new ManualTimeProvider();
        var executor = new Executor(new ErrorPolicyCatalog(), timeProvider: clock);

        Task<object?> run = await StartAsync(executor, HandlerFor<ClassLongMethodShortHandler>());
        clock.Advance(Short);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(() => run);
        HandlerTimedOut timedOut = Assert.IsType<HandlerTimedOut>(fault.InnerException);
        Assert.Equal(Short, timedOut.Timeout);
    }

    [Fact]
    public async Task method_attribute_overrides_a_shorter_class_attribute()
    {
        var clock = new ManualTimeProvider();
        var executor = new Executor(new ErrorPolicyCatalog(), timeProvider: clock);

        Task<object?> run = await StartAsync(executor, HandlerFor<ClassShortMethodLongHandler>());
        clock.Advance(Short);
        TimeoutProbe.ReleaseGate();
        await run;

        Assert.Equal([false], TimeoutProbe.CancelledOnAttempt);
    }

    [Fact]
    public async Task host_handler_timeout_reaches_the_executor()
    {
        var clock = new ManualTimeProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.UseCabbie(options => options.HandlerTimeout = Short);
        using IHost host = builder.Build();
        Executor executor = host.Services.GetRequiredService<Executor>();

        Task<object?> run = await StartAsync(executor, HandlerFor<UnboundedBlockHandler>());
        clock.Advance(Short);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(() => run);
        Assert.IsType<HandlerTimedOut>(fault.InnerException);
    }

    [Fact]
    public async Task handler_that_ignores_its_token_and_finishes_succeeds()
    {
        var clock = new ManualTimeProvider();
        var executor = new Executor(new ErrorPolicyCatalog(), timeProvider: clock);

        Task<object?> run = await StartAsync(executor, HandlerFor<IgnoresTokenHandler>());
        clock.Advance(Short);
        TimeoutProbe.ReleaseGate();
        await run;

        Assert.Equal([true], TimeoutProbe.CancelledOnAttempt);
    }

    [Fact]
    public void handler_timeout_attribute_rejects_non_positive_milliseconds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HandlerTimeoutAttribute(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HandlerTimeoutAttribute(-5));
    }

    [Fact]
    public void non_positive_or_unrepresentable_handler_timeout_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Host.CreateApplicationBuilder().UseCabbie(options => options.HandlerTimeout = TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Executor(new ErrorPolicyCatalog(), handlerTimeout: TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Executor(new ErrorPolicyCatalog(), handlerTimeout: TimeSpan.MaxValue));
    }

    /// <summary>Starts the invocation and returns once the handler is running, so the clock can be advanced.</summary>
    private static async Task<Task<object?>> StartAsync(
        Executor executor,
        DiscoveredHandler handler,
        CancellationToken cancellationToken = default)
    {
        Task<object?> run = executor.InvokeAsync(ChargePaymentEnvelope(), handler, cancellationToken);
        await TimeoutProbe.WaitStartedAsync();
        return run;
    }

    private static DiscoveredHandler HandlerFor<THandler>()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(THandler));
        return Assert.Single(catalog.Handlers);
    }

    private static Envelope ChargePaymentEnvelope() =>
        EnvelopeFactory.Create(
            message: new Message(new ChargePayment(1)),
            messageType: MessageTypeNaming.For(typeof(ChargePayment)));
}

/// <summary>Static probe shared by this class's handlers; tests in one class run sequentially.</summary>
internal static class TimeoutProbe
{
    private static readonly object Lock = new();
    private static readonly List<int> AttemptList = [];
    private static readonly List<bool> CancelledList = [];
    private static SemaphoreSlim _started = new(0);
    private static TaskCompletionSource _gate = NewGate();

    public static IReadOnlyList<int> Attempts
    {
        get
        {
            lock (Lock)
            {
                return [.. AttemptList];
            }
        }
    }

    public static IReadOnlyList<bool> CancelledOnAttempt
    {
        get
        {
            lock (Lock)
            {
                return [.. CancelledList];
            }
        }
    }

    public static Task GateTask => _gate.Task;

    public static void Reset()
    {
        lock (Lock)
        {
            AttemptList.Clear();
            CancelledList.Clear();
            _started = new SemaphoreSlim(0);
            _gate = NewGate();
        }
    }

    public static void Started(int attempt)
    {
        lock (Lock)
        {
            AttemptList.Add(attempt);
        }

        _started.Release();
    }

    public static Task WaitStartedAsync() => _started.WaitAsync(TimeSpan.FromSeconds(10));

    public static void ReleaseGate() => _gate.TrySetResult();

    public static void Cancelled(bool value)
    {
        lock (Lock)
        {
            CancelledList.Add(value);
        }
    }

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

[HandlerTimeout(50)]
public sealed class BlocksOnFirstAttemptHandler
{
    public async Task Handle(ChargePayment payment, Envelope envelope, CancellationToken cancellationToken)
    {
        TimeoutProbe.Started(envelope.Attempts.Value);
        try
        {
            if (envelope.Attempts.Value > 1)
            {
                // A token shared with the timed-out first attempt would already be cancelled here.
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        finally
        {
            TimeoutProbe.Cancelled(cancellationToken.IsCancellationRequested);
        }
    }
}

[HandlerTimeout(50)]
public sealed class AlwaysBlocksHandler
{
    public async Task Handle(ChargePayment payment, Envelope envelope, CancellationToken cancellationToken)
    {
        TimeoutProbe.Started(envelope.Attempts.Value);
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}

public sealed class UnboundedBlockHandler
{
    public async Task Handle(ChargePayment payment, Envelope envelope, CancellationToken cancellationToken)
    {
        TimeoutProbe.Started(envelope.Attempts.Value);
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}

[HandlerTimeout(10_000)]
public sealed class SlowButAllowedHandler
{
    public async Task Handle(ChargePayment payment, Envelope envelope, CancellationToken cancellationToken)
    {
        TimeoutProbe.Started(envelope.Attempts.Value);
        await TimeoutProbe.GateTask;
        TimeoutProbe.Cancelled(cancellationToken.IsCancellationRequested);
    }
}

[HandlerTimeout(10_000)]
public sealed class ClassLongMethodShortHandler
{
    [HandlerTimeout(50)]
    public async Task Handle(ChargePayment payment, Envelope envelope, CancellationToken cancellationToken)
    {
        TimeoutProbe.Started(envelope.Attempts.Value);
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}

[HandlerTimeout(50)]
public sealed class ClassShortMethodLongHandler
{
    [HandlerTimeout(10_000)]
    public async Task Handle(ChargePayment payment, Envelope envelope, CancellationToken cancellationToken)
    {
        TimeoutProbe.Started(envelope.Attempts.Value);
        await TimeoutProbe.GateTask;
        TimeoutProbe.Cancelled(cancellationToken.IsCancellationRequested);
    }
}

[HandlerTimeout(50)]
public sealed class IgnoresTokenHandler
{
    public async Task Handle(ChargePayment payment, Envelope envelope, CancellationToken cancellationToken)
    {
        TimeoutProbe.Started(envelope.Attempts.Value);
        await TimeoutProbe.GateTask;
        TimeoutProbe.Cancelled(cancellationToken.IsCancellationRequested);
    }
}
