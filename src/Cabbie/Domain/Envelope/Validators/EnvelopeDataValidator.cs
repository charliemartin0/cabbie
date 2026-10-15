using FluentValidation;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Domain.Envelope.Validators;

public sealed class EnvelopeDataValidator : AbstractValidator<EnvelopeData>
{
    public EnvelopeDataValidator()
    {
        // Empty is valid until Serialization has run. Pairing with ContentType is on EnvelopeValidator.
    }
}
