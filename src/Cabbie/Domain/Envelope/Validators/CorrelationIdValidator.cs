using FluentValidation;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Domain.Envelope.Validators;

public sealed class CorrelationIdValidator : AbstractValidator<CorrelationId>
{
    public CorrelationIdValidator()
    {
        RuleFor(x => x.Value)
            .NotEmpty()
            .WithMessage("CorrelationId must not be an empty Guid.");
    }
}
