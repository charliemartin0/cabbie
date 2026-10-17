using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Middleware;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Tests.Application.Mediator;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Middleware;

public sealed class ExecutorMiddlewareTests
{
    public ExecutorMiddlewareTests()
    {
        RetryingChargePaymentHandler.SeenAttempts.Clear();
        SucceedingPaymentHandler.Calls = 0;
        SecondFanOutHandler.Handled = false;
        ReturnsChargePaymentHandler.Returned = false;
    }

    [Fact]
    public async Task inner_wrapper_runs_around_handle_without_the_handler_knowing()
    {
        var middleware = new MiddlewareCatalog();
        var inner = new RecordingMiddleware();
        middleware.Register(MiddlewareLayer.Inner, inner);
        var executor = new Executor(new ErrorPolicyCatalog(), middleware: middleware);

        object? result = await executor.InvokeAsync(ChargePaymentEnvelope(), HandlerFor<SucceedingPaymentHandler>());

        Assert.Equal(1, inner.Calls);
        Assert.Equal(1, SucceedingPaymentHandler.Calls);
        Assert.IsType<ChargePayment>(result);
    }

    [Fact]
    public async Task outer_runs_once_and_inner_once_per_attempt()
    {
        var middleware = new MiddlewareCatalog();
        var outer = new RecordingMiddleware();
        var inner = new RecordingMiddleware();
        middleware.Register(MiddlewareLayer.Outer, outer);
        middleware.Register(MiddlewareLayer.Inner, inner);
        ErrorPolicyCatalog policies = TimeoutRetries();
        var executor = new Executor(policies, middleware: middleware);

        await executor.InvokeAsync(ChargePaymentEnvelope(), HandlerFor<RetryingChargePaymentHandler>());

        Assert.Equal(1, outer.Calls);
        Assert.Equal(3, inner.Calls);
        Assert.Equal([1, 2, 3], RetryingChargePaymentHandler.SeenAttempts);
    }

    [Fact]
    public async Task same_inner_instance_runs_on_every_retry()
    {
        var middleware = new MiddlewareCatalog();
        var inner = new RecordingMiddleware();
        middleware.Register(MiddlewareLayer.Inner, inner);
        var executor = new Executor(TimeoutRetries(), middleware: middleware);

        await executor.InvokeAsync(ChargePaymentEnvelope(), HandlerFor<RetryingChargePaymentHandler>());

        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task unnamed_outer_throw_after_next_does_not_retry_the_handler()
    {
        var middleware = new MiddlewareCatalog();
        middleware.Register(MiddlewareLayer.Outer, new ThrowAfterNextMiddleware(new InvalidOperationException("outer")));
        var executor = new Executor(TimeoutRetries(), middleware: middleware);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => executor.InvokeAsync(ChargePaymentEnvelope(), HandlerFor<SucceedingPaymentHandler>()));

        Assert.IsType<InvalidOperationException>(fault.InnerException);
        Assert.Equal(1, SucceedingPaymentHandler.Calls);
    }

    [Fact]
    public async Task unnamed_outer_throw_before_next_does_not_run_the_handler()
    {
        var middleware = new MiddlewareCatalog();
        middleware.Register(MiddlewareLayer.Outer, new ThrowBeforeNextMiddleware(new InvalidOperationException("outer")));
        var executor = new Executor(TimeoutRetries(), middleware: middleware);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => executor.InvokeAsync(ChargePaymentEnvelope(), HandlerFor<RetryingChargePaymentHandler>()));

        Assert.IsType<InvalidOperationException>(fault.InnerException);
        Assert.Empty(RetryingChargePaymentHandler.SeenAttempts);
    }

    [Fact]
    public async Task handler_fault_from_retries_is_not_nested()
    {
        var middleware = new MiddlewareCatalog();
        middleware.Register(MiddlewareLayer.Outer, new RecordingMiddleware());
        var executor = new Executor(new ErrorPolicyCatalog(), middleware: middleware);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => executor.InvokeAsync(ChargePaymentEnvelope(), HandlerFor<AlwaysTimeoutPaymentHandler>()));

        Assert.IsType<TimeoutException>(fault.InnerException);
        Assert.IsNotType<HandlerFault>(fault.InnerException);
    }

    [Fact]
    public async Task inner_next_violation_is_not_retried()
    {
        var middleware = new MiddlewareCatalog();
        middleware.Register(MiddlewareLayer.Inner, new SkipNextMiddleware());
        var executor = new Executor(TimeoutRetries(), middleware: middleware);

        await Assert.ThrowsAsync<MiddlewareNextViolation>(
            () => executor.InvokeAsync(ChargePaymentEnvelope(), HandlerFor<RetryingChargePaymentHandler>()));

        Assert.Empty(RetryingChargePaymentHandler.SeenAttempts);
    }

    [Fact]
    public async Task cancelled_invoke_throws_operation_canceled_not_next_violation()
    {
        var middleware = new MiddlewareCatalog();
        middleware.Register(MiddlewareLayer.Inner, new RecordingMiddleware());
        var executor = new Executor(new ErrorPolicyCatalog(), middleware: middleware);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.InvokeAsync(
                ChargePaymentEnvelope(),
                HandlerFor<SucceedingPaymentHandler>(),
                cts.Token));
    }

    [Fact]
    public async Task missing_handler_is_not_wrapped()
    {
        var middleware = new MiddlewareCatalog();
        var inner = new RecordingMiddleware();
        var outer = new RecordingMiddleware();
        middleware.Register(MiddlewareLayer.Inner, inner);
        middleware.Register(MiddlewareLayer.Outer, outer);
        var executor = new Executor(new ErrorPolicyCatalog(), middleware: middleware);

        HandlerNotFound missing = await Assert.ThrowsAsync<HandlerNotFound>(
            () => executor.HandleMissingAsync(ChargePaymentEnvelope()));

        Assert.Equal(typeof(ChargePayment), missing.MessageType);
        Assert.Equal(0, inner.Calls);
        Assert.Equal(0, outer.Calls);
    }

    [Fact]
    public async Task invoke_publishes_cascades_after_outer_returns()
    {
        var middleware = new MiddlewareCatalog();
        middleware.Register(MiddlewareLayer.Outer, new RecordingMiddleware());
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ReturnsChargePaymentHandler));
        var cascades = new RecordingCascadePublisher();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(
            catalog,
            cascades,
            new Executor(new ErrorPolicyCatalog(), middleware: middleware));

        await bus.InvokeAsync(new PlaceOrder(1));

        var payment = Assert.IsType<ChargePayment>(Assert.Single(cascades.Published));
        Assert.Equal(1, payment.OrderId);
    }

    [Fact]
    public async Task outer_throw_publishes_no_cascades()
    {
        var middleware = new MiddlewareCatalog();
        middleware.Register(MiddlewareLayer.Outer, new ThrowAfterNextMiddleware(new InvalidOperationException("outer")));
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ReturnsChargePaymentHandler));
        var cascades = new RecordingCascadePublisher();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(
            catalog,
            cascades,
            new Executor(new ErrorPolicyCatalog(), middleware: middleware));

        await Assert.ThrowsAsync<HandlerFault>(() => bus.InvokeAsync(new PlaceOrder(1)));

        Assert.Empty(cascades.Published);
    }

    [Fact]
    public async Task fan_out_wraps_each_handler_and_aborts_on_the_first_throw()
    {
        var middleware = new MiddlewareCatalog();
        var inner = new RecordingMiddleware();
        middleware.Register(MiddlewareLayer.Inner, inner);
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ThrowingFirstFanOutHandler));
        catalog.Scan(typeof(SecondFanOutHandler));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(
            catalog,
            executor: new Executor(new ErrorPolicyCatalog(), middleware: middleware));

        await Assert.ThrowsAsync<HandlerFault>(() => bus.InvokeAsync(new ChargePayment(1)));

        Assert.Equal(typeof(ThrowingFirstFanOutHandler), Assert.Single(inner.HandlerTypes));
        Assert.False(SecondFanOutHandler.Handled);
    }

    [Fact]
    public async Task fan_out_success_wraps_each_handler()
    {
        var middleware = new MiddlewareCatalog();
        var inner = new RecordingMiddleware();
        middleware.Register(MiddlewareLayer.Inner, inner);
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(FirstFanOutHandler));
        catalog.Scan(typeof(SecondFanOutHandler));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(
            catalog,
            executor: new Executor(new ErrorPolicyCatalog(), middleware: middleware));

        await bus.InvokeAsync(new ChargePayment(1));

        Assert.Equal(
            [typeof(FirstFanOutHandler), typeof(SecondFanOutHandler)],
            inner.HandlerTypes);
        Assert.True(SecondFanOutHandler.Handled);
    }

    private static ErrorPolicyCatalog TimeoutRetries()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Retry().Retry();
        return policies;
    }

    private static Envelope ChargePaymentEnvelope() =>
        EnvelopeFactory.Create(
            message: new Message(new ChargePayment(1)),
            messageType: MessageTypeNaming.For(typeof(ChargePayment)));

    private static DiscoveredHandler HandlerFor<THandler>()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(THandler));
        return Assert.Single(catalog.Handlers);
    }
}

public sealed class SucceedingPaymentHandler
{
    public static int Calls;

    public ChargePayment Handle(ChargePayment payment)
    {
        Calls++;
        return payment;
    }
}

public sealed class RetryingChargePaymentHandler
{
    public static List<int> SeenAttempts { get; } = [];

    public ChargePayment Handle(ChargePayment payment, Envelope envelope)
    {
        SeenAttempts.Add(envelope.Attempts.Value);
        if (envelope.Attempts.Value < 3)
        {
            throw new TimeoutException("Payment gateway timeout");
        }

        return payment;
    }
}

public sealed class AlwaysTimeoutPaymentHandler
{
    public void Handle(ChargePayment payment) => throw new TimeoutException("Payment gateway timeout");
}

public sealed class ThrowAfterNextMiddleware : IMessageMiddleware
{
    private readonly Exception _exception;

    public ThrowAfterNextMiddleware(Exception exception) => _exception = exception;

    public async Task<object?> InvokeAsync(
        Envelope envelope,
        DiscoveredHandler handler,
        Func<Task<object?>> next,
        CancellationToken cancellationToken)
    {
        _ = await next();
        throw _exception;
    }
}

public sealed class ThrowBeforeNextMiddleware : IMessageMiddleware
{
    private readonly Exception _exception;

    public ThrowBeforeNextMiddleware(Exception exception) => _exception = exception;

    public Task<object?> InvokeAsync(
        Envelope envelope,
        DiscoveredHandler handler,
        Func<Task<object?>> next,
        CancellationToken cancellationToken) =>
        throw _exception;
}

public sealed class ThrowingFirstFanOutHandler
{
    public void Handle(ChargePayment payment) => throw new TimeoutException("first");
}

public sealed class FirstFanOutHandler
{
    public void Handle(ChargePayment payment)
    {
    }
}

public sealed class SecondFanOutHandler
{
    public static bool Handled;

    public void Handle(ChargePayment payment) => Handled = true;
}
