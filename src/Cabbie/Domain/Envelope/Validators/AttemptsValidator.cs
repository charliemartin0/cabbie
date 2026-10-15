using FluentValidation;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Domain.Envelope.Validators;

public sealed class AttemptsValidator : AbstractValidator<Attempts>
{
    public AttemptsValidator()
    {
        RuleFor(x => x.Value)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Attempts must be at least 1. The first execution is attempt 1.");
    }
}
