using Cabbie.Application.Execution;
using Cabbie.Domain.Errors.ValueObjects;
using Cabbie.Tests.Domain;

namespace Cabbie.Tests.Application.Execution;

public sealed class ErrorPolicyCatalogTests
{
    [Fact]
    public void timeout_exception_chain_is_retry_with_cooldown_then_error_queue()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>()
            .RetryWithCooldown(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(500))
            .Then
            .MoveToErrorQueue();

        var found = Assert.IsType<FoundErrorPolicy>(policies.For(typeof(TimeoutException)));

        Assert.Equal(
            [
                new RetryWithCooldown(TimeSpan.FromMilliseconds(100)),
                new RetryWithCooldown(TimeSpan.FromMilliseconds(250)),
                new RetryWithCooldown(TimeSpan.FromMilliseconds(500)),
                new MoveToErrorQueue()
            ],
            found.Actions);
    }

    [Fact]
    public void unconfigured_exception_is_a_missing_policy_not_a_throw()
    {
        var policies = new ErrorPolicyCatalog();

        ErrorPolicyLookup lookup = policies.For(typeof(InvalidOperationException));

        var missing = Assert.IsType<MissingErrorPolicy>(lookup);
        Assert.Equal(typeof(InvalidOperationException), missing.ExceptionType);
    }

    [Fact]
    public void message_type_rule_overrides_global_exception_rule()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().MoveToErrorQueue();
        policies.OnException<TimeoutException, ChargePayment>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(100))
            .Then
            .MoveToErrorQueue();

        var forPayment = Assert.IsType<FoundErrorPolicy>(
            policies.For(typeof(TimeoutException), typeof(ChargePayment)));
        Assert.Equal(
            [new RetryWithCooldown(TimeSpan.FromMilliseconds(100)), new MoveToErrorQueue()],
            forPayment.Actions);

        var forPlaceOrder = Assert.IsType<FoundErrorPolicy>(
            policies.For(typeof(TimeoutException), typeof(PlaceOrder)));
        Assert.Equal([new MoveToErrorQueue()], forPlaceOrder.Actions);
    }

    [Fact]
    public void for_exception_type_alone_does_not_use_a_message_specific_rule()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException, ChargePayment>().Retry();

        Assert.IsType<MissingErrorPolicy>(policies.For(typeof(TimeoutException)));
        Assert.Equal(
            [new Retry()],
            Assert.IsType<FoundErrorPolicy>(policies.For(typeof(TimeoutException), typeof(ChargePayment))).Actions);
    }

    [Fact]
    public void retry_now_is_immediate_retry_without_cooldown()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Retry().Retry().Then.MoveToErrorQueue();

        var found = Assert.IsType<FoundErrorPolicy>(policies.For(typeof(TimeoutException)));

        Assert.Equal([new Retry(), new Retry(), new MoveToErrorQueue()], found.Actions);
    }

    [Fact]
    public void requeue_schedule_retry_and_discard_are_named_actions()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Requeue();
        policies.OnException<InvalidOperationException>().ScheduleRetry(TimeSpan.FromSeconds(5));
        policies.OnException<ArgumentException>().Discard();

        Assert.Equal(
            [new Requeue()],
            Assert.IsType<FoundErrorPolicy>(policies.For(typeof(TimeoutException))).Actions);
        Assert.Equal(
            [new ScheduleRetry(TimeSpan.FromSeconds(5))],
            Assert.IsType<FoundErrorPolicy>(policies.For(typeof(InvalidOperationException))).Actions);
        Assert.Equal(
            [new Discard()],
            Assert.IsType<FoundErrorPolicy>(policies.For(typeof(ArgumentException))).Actions);
    }

    [Fact]
    public void schedule_retry_registers_one_action_per_delay()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>()
            .ScheduleRetry(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

        Assert.Equal(
            [new ScheduleRetry(TimeSpan.FromSeconds(5)), new ScheduleRetry(TimeSpan.FromSeconds(30))],
            Assert.IsType<FoundErrorPolicy>(policies.For(typeof(TimeoutException))).Actions);
    }

    [Fact]
    public void schedule_retry_rejects_empty_or_non_positive_delays()
    {
        var policies = new ErrorPolicyCatalog();

        Assert.Throws<ArgumentException>(() => policies.OnException<TimeoutException>().ScheduleRetry());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => policies.OnException<TimeoutException>().ScheduleRetry(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => policies.OnException<TimeoutException>().ScheduleRetry(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void null_arguments_throw()
    {
        var policies = new ErrorPolicyCatalog();

        Assert.Throws<ArgumentNullException>(() => policies.OnException(null!));
        Assert.Throws<ArgumentNullException>(() => policies.OnException(typeof(TimeoutException), null!));
        Assert.Throws<ArgumentNullException>(() => policies.For((Type)null!));
        Assert.Throws<ArgumentNullException>(() => policies.For(typeof(TimeoutException), null!));
        Assert.Throws<ArgumentNullException>(() => policies.OnException<TimeoutException>().RetryWithCooldown(null!));
    }

    [Fact]
    public void negative_cooldown_still_registers()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().RetryWithCooldown(TimeSpan.FromTicks(-1));

        var found = Assert.IsType<FoundErrorPolicy>(policies.For(typeof(TimeoutException)));
        Assert.Equal([new RetryWithCooldown(TimeSpan.FromTicks(-1))], found.Actions);
    }
}
