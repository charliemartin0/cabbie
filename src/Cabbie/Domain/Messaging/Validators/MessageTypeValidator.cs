using FluentValidation;
using Cabbie.Domain.Messaging.ValueObjects;

namespace Cabbie.Domain.Messaging.Validators;

public sealed class MessageTypeValidator : AbstractValidator<MessageType>
{
    public MessageTypeValidator()
    {
        RuleFor(x => x.Value)
            .NotEmpty()
            .WithMessage("MessageType must not be empty.")
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("MessageType must not be whitespace.");
    }
}
