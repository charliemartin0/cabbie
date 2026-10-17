using Cabbie.Domain.Sagas.ValueObjects;
using FluentValidation;

namespace Cabbie.Domain.Sagas.Validators;

public sealed class SagaIdValidator : AbstractValidator<SagaId>
{
    public SagaIdValidator()
    {
        RuleFor(x => x.Value)
            .NotNull()
            .WithMessage("SagaId must not be null. Use an empty string when the message is not part of a saga.")
            .Must(value => value.Length == 0 || !string.IsNullOrWhiteSpace(value))
            .WithMessage("SagaId must be empty (no saga) or a non-whitespace id.");
    }
}
