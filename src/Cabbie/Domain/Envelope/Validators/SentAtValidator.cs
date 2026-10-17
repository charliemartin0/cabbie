using FluentValidation;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Domain.Envelope.Validators;

public sealed class SentAtValidator : AbstractValidator<SentAt>
{
    public SentAtValidator()
    {
        RuleFor(x => x.Value)
            .NotEqual(default(DateTimeOffset))
            .WithMessage("SentAt must not be default.")
            .Must(value => value <= DateTimeOffset.UtcNow.AddDays(1))
            .WithMessage("SentAt must not be in the far future.");
    }
}
