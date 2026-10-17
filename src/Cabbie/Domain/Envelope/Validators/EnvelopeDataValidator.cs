using Cabbie.Domain.Envelope.ValueObjects;
using FluentValidation;

namespace Cabbie.Domain.Envelope.Validators;

public sealed class EnvelopeDataValidator : AbstractValidator<EnvelopeData>
{
    public EnvelopeDataValidator()
    {
        // Empty is valid until Serialization has run. Pairing with ContentType is on EnvelopeValidator.
    }
}
