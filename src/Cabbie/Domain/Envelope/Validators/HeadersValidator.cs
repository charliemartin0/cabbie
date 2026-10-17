using Cabbie.Domain.Envelope.ValueObjects;
using FluentValidation;

namespace Cabbie.Domain.Envelope.Validators;

public sealed class HeadersValidator : AbstractValidator<Headers>
{
    public HeadersValidator()
    {
        RuleFor(x => x.Value)
            .NotNull()
            .WithMessage("Headers must not be null.");

        When(x => x.Value is not null, () =>
        {
            RuleForEach(x => x.Value.Keys)
                .NotEmpty()
                .WithMessage("Header keys must not be empty.")
                .Must(key => !string.IsNullOrWhiteSpace(key))
                .WithMessage("Header keys must not be whitespace.");
        });
    }
}
