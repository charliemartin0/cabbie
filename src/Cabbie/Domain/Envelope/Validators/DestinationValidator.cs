using Cabbie.Domain.Envelope.ValueObjects;
using FluentValidation;

namespace Cabbie.Domain.Envelope.Validators;

public sealed class DestinationValidator : AbstractValidator<Destination>
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "local",
        "tcp",
        "rabbitmq"
    };

    public DestinationValidator()
    {
        RuleFor(x => x.Value)
            .NotNull()
            .WithMessage("Destination must not be null.");

        When(x => x.Value is not null, () =>
        {
            RuleFor(x => x.Value)
                .Must(uri => uri.IsAbsoluteUri)
                .WithMessage("Destination must be an absolute URI.");

            RuleFor(x => x.Value.Scheme)
                .Must(scheme => AllowedSchemes.Contains(scheme))
                .WithMessage("Destination scheme must be local, tcp, or rabbitmq.");
        });
    }
}
