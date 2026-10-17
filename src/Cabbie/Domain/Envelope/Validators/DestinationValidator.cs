using Cabbie.Domain.Envelope.ValueObjects;
using FluentValidation;

namespace Cabbie.Domain.Envelope.Validators;

/// <summary>
/// A destination is a non-null absolute URI. Which schemes are deliverable is not a Domain
/// rule: the registered transports decide, and an endpoint whose scheme has no transport
/// fails with <c>UnknownTransportScheme</c> when the host starts.
/// </summary>
public sealed class DestinationValidator : AbstractValidator<Destination>
{
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
        });
    }
}
