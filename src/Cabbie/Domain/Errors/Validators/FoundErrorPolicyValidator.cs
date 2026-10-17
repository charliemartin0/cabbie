using Cabbie.Domain.Errors.ValueObjects;
using FluentValidation;

namespace Cabbie.Domain.Errors.Validators;

public sealed class FoundErrorPolicyValidator : AbstractValidator<FoundErrorPolicy>
{
    public FoundErrorPolicyValidator()
    {
        RuleFor(x => x.Actions)
            .NotNull()
            .WithMessage("Found error policy actions must not be null.")
            .NotEmpty()
            .WithMessage("Found error policy must include at least one action.");
    }
}
