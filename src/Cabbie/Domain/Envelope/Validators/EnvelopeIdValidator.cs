using Cabbie.Domain.Envelope.ValueObjects;
using FluentValidation;

namespace Cabbie.Domain.Envelope.Validators;

public sealed class EnvelopeIdValidator : AbstractValidator<EnvelopeId>
{
    public EnvelopeIdValidator()
    {
        RuleFor(x => x.Value)
            .NotEmpty()
            .WithMessage("EnvelopeId must not be an empty Guid.");
    }
}
