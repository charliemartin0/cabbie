using Cabbie.Domain.Errors.Validators;
using Cabbie.Domain.Errors.ValueObjects;
using FluentValidation.TestHelper;

namespace Cabbie.Tests.Domain.Errors;

public sealed class ErrorValueObjectValidatorTests
{
    [Fact]
    public void negative_cooldown_is_rejected()
    {
        var result = new RetryWithCooldownValidator().TestValidate(
            new RetryWithCooldown(TimeSpan.FromTicks(-1)));

        result.ShouldHaveValidationErrorFor(x => x.Delay);
    }

    [Fact]
    public void zero_cooldown_passes()
    {
        var result = new RetryWithCooldownValidator().TestValidate(
            new RetryWithCooldown(TimeSpan.Zero));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void zero_schedule_retry_is_rejected()
    {
        var result = new ScheduleRetryValidator().TestValidate(new ScheduleRetry(TimeSpan.Zero));

        result.ShouldHaveValidationErrorFor(x => x.Delay);
    }

    [Fact]
    public void negative_schedule_retry_is_rejected()
    {
        var result = new ScheduleRetryValidator().TestValidate(
            new ScheduleRetry(TimeSpan.FromTicks(-1)));

        result.ShouldHaveValidationErrorFor(x => x.Delay);
    }

    [Fact]
    public void positive_schedule_retry_passes()
    {
        var result = new ScheduleRetryValidator().TestValidate(
            new ScheduleRetry(TimeSpan.FromSeconds(5)));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void positive_cooldown_passes()
    {
        var result = new RetryWithCooldownValidator().TestValidate(
            new RetryWithCooldown(TimeSpan.FromMilliseconds(100)));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void found_policy_with_null_actions_is_rejected()
    {
        var result = new FoundErrorPolicyValidator().TestValidate(
            new FoundErrorPolicy(null!));

        result.ShouldHaveValidationErrorFor(x => x.Actions);
    }

    [Fact]
    public void found_policy_with_empty_actions_is_rejected()
    {
        var result = new FoundErrorPolicyValidator().TestValidate(
            new FoundErrorPolicy([]));

        result.ShouldHaveValidationErrorFor(x => x.Actions);
    }

    [Fact]
    public void found_policy_with_actions_passes()
    {
        var result = new FoundErrorPolicyValidator().TestValidate(
            new FoundErrorPolicy([new Retry()]));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void missing_policy_with_null_exception_type_is_rejected()
    {
        var result = new MissingErrorPolicyValidator().TestValidate(
            new MissingErrorPolicy(null!));

        result.ShouldHaveValidationErrorFor(x => x.ExceptionType);
    }

    [Fact]
    public void missing_policy_with_exception_type_passes()
    {
        var result = new MissingErrorPolicyValidator().TestValidate(
            new MissingErrorPolicy(typeof(TimeoutException)));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
