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
    public void unconfigured_exception_is_the_built_in_default_policy_not_a_throw()
    {
        var policies = new ErrorPolicyCatalog();

        ErrorPolicyLookup lookup = policies.For(typeof(InvalidOperationException));

        var found = Assert.IsType<FoundErrorPolicy>(lookup);
        Assert.Equal(
            [
                new RetryWithCooldown(TimeSpan.FromMilliseconds(100)),
                new RetryWithCooldown(TimeSpan.FromMilliseconds(500)),
                new RetryWithCooldown(TimeSpan.FromSeconds(2)),
                new MoveToErrorQueue()
            ],
            found.Actions);
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
        policies.OnUnmatchedException();
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
            [new Requeue(TimeSpan.Zero)],
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

    [Fact]
    public void io_exception_policy_matches_file_not_found_exception()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<IOException>().Retry();

        var found = Assert.IsType<FoundErrorPolicy>(policies.For(typeof(FileNotFoundException)));
        Assert.Equal([new Retry()], found.Actions);
    }

    [Fact]
    public void closest_base_type_policy_wins_over_more_general_one()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<Exception>().Discard();
        policies.OnException<IOException>().Retry();

        var found = Assert.IsType<FoundErrorPolicy>(policies.For(typeof(FileNotFoundException)));
        Assert.Equal([new Retry()], found.Actions);
    }

    [Fact]
    public void message_scoped_rule_beats_global_rule_at_same_distance()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<IOException>().Discard();
        policies.OnException<IOException, ChargePayment>().Retry();

        var scoped = Assert.IsType<FoundErrorPolicy>(
            policies.For(typeof(FileNotFoundException), typeof(ChargePayment)));
        Assert.Equal([new Retry()], scoped.Actions);

        var other = Assert.IsType<FoundErrorPolicy>(
            policies.For(typeof(FileNotFoundException), typeof(PlaceOrder)));
        Assert.Equal([new Discard()], other.Actions);
    }

    [Fact]
    public void closer_global_rule_beats_message_scoped_rule_on_a_base_type()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<FileNotFoundException>().Discard();
        policies.OnException<IOException, ChargePayment>().Retry();

        var found = Assert.IsType<FoundErrorPolicy>(
            policies.For(typeof(FileNotFoundException), typeof(ChargePayment)));
        Assert.Equal([new Discard()], found.Actions);
    }

    [Fact]
    public void on_unmatched_exception_replaces_the_default()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnUnmatchedException().Retry().Then.Discard();

        var found = Assert.IsType<FoundErrorPolicy>(policies.For(typeof(InvalidOperationException)));
        Assert.Equal([new Retry(), new Discard()], found.Actions);
    }

    [Fact]
    public void empty_unmatched_policy_is_missing()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnUnmatchedException();

        var missing = Assert.IsType<MissingErrorPolicy>(policies.For(typeof(InvalidOperationException)));
        Assert.Equal(typeof(InvalidOperationException), missing.ExceptionType);
    }

    [Fact]
    public void repeated_on_unmatched_exception_calls_append_to_one_chain()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnUnmatchedException().Retry();
        policies.OnUnmatchedException().Discard();

        var found = Assert.IsType<FoundErrorPolicy>(policies.For(typeof(InvalidOperationException)));
        Assert.Equal([new Retry(), new Discard()], found.Actions);
    }

    [Fact]
    public void default_can_be_withheld_so_invoke_style_lookups_fail_fast()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<IOException>().Retry();

        Assert.IsType<MissingErrorPolicy>(policies.For(typeof(InvalidOperationException), useDefault: false));
        Assert.IsType<FoundErrorPolicy>(policies.For(typeof(FileNotFoundException), useDefault: false));
    }

    [Fact]
    public void default_retry_delays_become_one_retry_each_then_dead_letter()
    {
        var policies = new ErrorPolicyCatalog();
        policies.UseDefaultRetryDelays([TimeSpan.FromSeconds(3)]);

        var found = Assert.IsType<FoundErrorPolicy>(policies.For(typeof(InvalidOperationException)));
        Assert.Equal([new RetryWithCooldown(TimeSpan.FromSeconds(3)), new MoveToErrorQueue()], found.Actions);
    }

    [Fact]
    public void default_retry_delays_do_not_override_an_unmatched_policy()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnUnmatchedException().Discard();
        policies.UseDefaultRetryDelays([TimeSpan.FromSeconds(3)]);

        var found = Assert.IsType<FoundErrorPolicy>(policies.For(typeof(InvalidOperationException)));
        Assert.Equal([new Discard()], found.Actions);
    }

    [Fact]
    public void negative_default_retry_delay_is_rejected()
    {
        var policies = new ErrorPolicyCatalog();

        Assert.Throws<ArgumentOutOfRangeException>(() => policies.UseDefaultRetryDelays([TimeSpan.FromSeconds(-1)]));
    }
}
