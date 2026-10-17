using Cabbie.Domain.Errors.ValueObjects;
using FluentValidation;

namespace Cabbie.Domain.Errors.Validators;

public sealed class MissingErrorPolicyValidator : AbstractValidator<MissingErrorPolicy>
{
    public MissingErrorPolicyValidator()
    {
        RuleFor(x => x.ExceptionType)
            .NotNull()
            .WithMessage("Missing error policy exception type must not be null.");
    }
}
