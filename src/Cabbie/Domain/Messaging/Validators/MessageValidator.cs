using FluentValidation;
using Cabbie.Domain.Messaging.ValueObjects;

namespace Cabbie.Domain.Messaging.Validators;

public sealed class MessageValidator : AbstractValidator<Message>
{
    public MessageValidator()
    {
        RuleFor(x => x.Value)
            .NotNull()
            .WithMessage("Message body must not be null.");
    }
}
