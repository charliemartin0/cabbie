using FluentValidation;
using Cabbie.Domain.Errors.ValueObjects;

namespace Cabbie.Domain.Errors.Validators;

public sealed class RetryWithCooldownValidator : AbstractValidator<RetryWithCooldown>
{
    public RetryWithCooldownValidator()
    {
        RuleFor(x => x.Delay)
            .GreaterThanOrEqualTo(TimeSpan.Zero)
            .WithMessage("RetryWithCooldown delay must not be negative.");
    }
}
