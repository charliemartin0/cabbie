using Cabbie.Domain.Envelope.ValueObjects;
using FluentValidation;

namespace Cabbie.Domain.Envelope.Validators;

public sealed class ConversationIdValidator : AbstractValidator<ConversationId>
{
    public ConversationIdValidator()
    {
        RuleFor(x => x.Value)
            .NotEmpty()
            .WithMessage("ConversationId must not be an empty Guid.");
    }
}
