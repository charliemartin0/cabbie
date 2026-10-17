using FluentValidation;
using Cabbie.Domain.Errors.ValueObjects;

namespace Cabbie.Domain.Errors.Validators;

public sealed class ScheduleRetryValidator : AbstractValidator<ScheduleRetry>
{
    public ScheduleRetryValidator()
    {
        RuleFor(x => x.Delay)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("ScheduleRetry delay must be greater than zero.");
    }
}
