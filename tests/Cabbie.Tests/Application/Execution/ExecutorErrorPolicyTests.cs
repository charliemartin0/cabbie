using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Tests.Application.Transports;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Tests.Application.Execution;

/// <summary>
/// Prove-withs for A7: the Discard and Requeue Domain vocabulary is honored by the
/// Executor's retry loop instead of falling through to HandlerFault.
/// </summary>
public sealed class ExecutorErrorPolicyTests
{
    public ExecutorErrorPolicyTests()
    {
        TimeoutThrowingHandler.SeenAttempts.Clear();
        CancelledOnFirstAttemptHandler.SeenAttempts.Clear();
    }

    [Fact]
    public async Task discard_returns_null_without_throwing()
    {
        Envelope envelope = ChargePaymentEnvelope();
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Discard();
        var executor = new Executor(policies);

        object? result = await executor.InvokeAsync(envelope, HandlerFor<TimeoutThrowingHandler>());

        Assert.Null(result);
        Assert.Equal([1], TimeoutThrowingHandler.SeenAttempts);
    }

    [Fact]
    public async Task requeue_re_sends_the_envelope_to_its_destination()
    {
        Envelope envelope = ChargePaymentEnvelope();
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Requeue();
        var sender = new RecordingSender();
        var executor = new Executor(policies, sender: () => sender);

        object? result = await executor.InvokeAsync(envelope, HandlerFor<TimeoutThrowingHandler>());

        // The invocation completes without a fault; the envelope went back to the queue.
        Assert.Null(result);
        Envelope requeued = Assert.Single(sender.Sent);
        Assert.Equal(envelope.Id, requeued.Id);
        Assert.Equal(envelope.Destination, requeued.Destination);

        // The handler was not re-invoked in-process.
        Assert.Equal([1], TimeoutThrowingHandler.SeenAttempts);
    }

    [Fact]
    public async Task unconfigured_invoke_async_fails_once_and_is_not_dead_lettered()
    {
        var errorQueue = new RecordingErrorQueue();
        var executor = new Executor(new ErrorPolicyCatalog(), errorQueue: errorQueue);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => executor.InvokeAsync(ChargePaymentEnvelope(), HandlerFor<TimeoutThrowingHandler>()));

        Assert.IsType<TimeoutException>(fault.InnerException);
        Assert.Equal([1], TimeoutThrowingHandler.SeenAttempts);
        Assert.Empty(errorQueue.Moved);
    }

    [Fact]
    public async Task explicit_rule_still_applies_to_invoke_async()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Retry().Retry();
        var executor = new Executor(policies);

        await Assert.ThrowsAsync<HandlerFault>(
            () => executor.InvokeAsync(ChargePaymentEnvelope(), HandlerFor<TimeoutThrowingHandler>()));

        Assert.Equal([1, 2, 3], TimeoutThrowingHandler.SeenAttempts);
    }

    [Fact]
    public async Task configured_unmatched_policy_retries_then_dead_letters_a_queue_delivery()
    {
        Envelope envelope = ChargePaymentEnvelope();
        var policies = new ErrorPolicyCatalog();
        policies.OnUnmatchedException().Retry().Then.MoveToErrorQueue();
        var errorQueue = new RecordingErrorQueue();
        var executor = new Executor(policies, errorQueue: errorQueue);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => executor.InvokeAsync(envelope, QueueHandlerFor<TimeoutThrowingHandler>()));

        Assert.IsType<TimeoutException>(fault.InnerException);
        Assert.Equal([1, 2], TimeoutThrowingHandler.SeenAttempts);
        Envelope moved = Assert.Single(errorQueue.Moved);
        Assert.Equal(envelope.Id, moved.Id);
    }

    [Fact]
    public async Task built_in_default_retries_a_queue_delivery_three_times_then_dead_letters()
    {
        Envelope envelope = ChargePaymentEnvelope();
        var errorQueue = new RecordingErrorQueue();
        var executor = new Executor(
            new ErrorPolicyCatalog(),
            errorQueue: errorQueue,
            timeProvider: new ManualTimeProvider(autoFire: true));

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => executor.InvokeAsync(envelope, QueueHandlerFor<TimeoutThrowingHandler>()));

        Assert.IsType<TimeoutException>(fault.InnerException);
        Assert.Equal([1, 2, 3, 4], TimeoutThrowingHandler.SeenAttempts);
        Envelope moved = Assert.Single(errorQueue.Moved);
        Assert.Equal(envelope.Id, moved.Id);
        Assert.Equal(4, moved.Attempts.Value);
    }

    [Fact]
    public async Task default_retry_delays_option_sets_the_number_of_retries_before_dead_letter()
    {
        var errorQueue = new RecordingErrorQueue();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<TimeProvider>(new ManualTimeProvider(autoFire: true));
        builder.UseCabbie(options => options.DefaultRetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
        builder.Services.AddSingleton<IErrorQueue>(errorQueue);
        using IHost host = builder.Build();
        Executor executor = host.Services.GetRequiredService<Executor>();

        await Assert.ThrowsAsync<HandlerFault>(
            () => executor.InvokeAsync(ChargePaymentEnvelope(), QueueHandlerFor<TimeoutThrowingHandler>()));

        Assert.Equal([1, 2, 3], TimeoutThrowingHandler.SeenAttempts);
        Assert.Single(errorQueue.Moved);
    }

    [Fact]
    public async Task empty_default_retry_delays_dead_letters_a_queue_delivery_on_the_first_failure()
    {
        var errorQueue = new RecordingErrorQueue();
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options => options.DefaultRetryDelays = []);
        builder.Services.AddSingleton<IErrorQueue>(errorQueue);
        using IHost host = builder.Build();
        Executor executor = host.Services.GetRequiredService<Executor>();

        await Assert.ThrowsAsync<HandlerFault>(
            () => executor.InvokeAsync(ChargePaymentEnvelope(), QueueHandlerFor<TimeoutThrowingHandler>()));

        Assert.Equal([1], TimeoutThrowingHandler.SeenAttempts);
        Assert.Single(errorQueue.Moved);
    }

    [Fact]
    public async Task cancellation_not_requested_by_the_caller_is_retried_per_policy()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TaskCanceledException>().Retry();
        var executor = new Executor(policies);

        await executor.InvokeAsync(ChargePaymentEnvelope(), HandlerFor<CancelledOnFirstAttemptHandler>());

        Assert.Equal([1, 2], CancelledOnFirstAttemptHandler.SeenAttempts);
    }

    [Fact]
    public async Task cancellation_requested_by_the_caller_is_not_retried()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TaskCanceledException>().Retry();
        var executor = new Executor(policies);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => executor.InvokeAsync(ChargePaymentEnvelope(), HandlerFor<CancelledOnFirstAttemptHandler>(), cts.Token));

        Assert.Empty(CancelledOnFirstAttemptHandler.SeenAttempts);
    }

    private static DiscoveredHandler QueueHandlerFor<THandler>() => HandlerFor<THandler>() with { Scheduled = true };

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

/// <summary>
/// Own fixture (not the shared ExecutorTests one) so the parallel test classes
/// never race on static handler state.
/// </summary>
public sealed class TimeoutThrowingHandler
{
    public static List<int> SeenAttempts { get; } = [];

    public void Handle(ChargePayment message, Envelope envelope)
    {
        SeenAttempts.Add(envelope.Attempts.Value);
        throw new TimeoutException("always");
    }
}

public sealed class CancelledOnFirstAttemptHandler
{
    public static List<int> SeenAttempts { get; } = [];

    public void Handle(ChargePayment message, Envelope envelope)
    {
        SeenAttempts.Add(envelope.Attempts.Value);
        if (envelope.Attempts.Value == 1)
        {
            // What an HTTP client timeout looks like: cancelled, but not by the caller.
            throw new TaskCanceledException("client timed out");
        }
    }
}