using FluentValidation;

namespace Cabbie.Domain.Messaging.Validators;

public sealed class MessageIdentityAttributeValidator : AbstractValidator<MessageIdentityAttribute>
{
    public MessageIdentityAttributeValidator()
    {
        RuleFor(x => x.Alias)
            .NotEmpty()
            .WithMessage("MessageIdentity alias must not be empty.")
            .Must(alias => !string.IsNullOrWhiteSpace(alias))
            .WithMessage("MessageIdentity alias must not be whitespace.");
    }
}
