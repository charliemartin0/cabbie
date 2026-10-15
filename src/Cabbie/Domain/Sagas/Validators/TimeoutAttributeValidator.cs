using FluentValidation;

namespace Cabbie.Domain.Sagas.Validators;

public sealed class TimeoutAttributeValidator : AbstractValidator<TimeoutAttribute>
{
    public TimeoutAttributeValidator()
    {
        RuleFor(x => x.Hours)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Timeout hours must not be negative.");

        RuleFor(x => x.Minutes)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Timeout minutes must not be negative.");

        RuleFor(x => x.Seconds)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Timeout seconds must not be negative.");

        RuleFor(x => x.Milliseconds)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Timeout milliseconds must not be negative.");

        RuleFor(x => x.Delay)
            .GreaterThan(TimeSpan.Zero)
            .When(x => x.Hours >= 0 && x.Minutes >= 0 && x.Seconds >= 0 && x.Milliseconds >= 0)
            .WithMessage("Timeout delay must be greater than zero.");
    }
}
