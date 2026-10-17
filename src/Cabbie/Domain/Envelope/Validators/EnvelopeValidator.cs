using Cabbie.Domain.Messaging.Validators;
using Cabbie.Domain.Sagas.Validators;
using FluentValidation;

namespace Cabbie.Domain.Envelope.Validators;

/// <summary>
/// Per-field rules live on the child validators. This type only composes them and
/// checks rules that span more than one value object.
/// </summary>
public sealed class EnvelopeValidator : AbstractValidator<Envelope>
{
    public EnvelopeValidator()
    {
        RuleFor(x => x.Id).SetValidator(new EnvelopeIdValidator());
        RuleFor(x => x.Message).SetValidator(new MessageValidator());
        RuleFor(x => x.MessageType).SetValidator(new MessageTypeValidator());
        RuleFor(x => x.Destination).SetValidator(new DestinationValidator());
        RuleFor(x => x.CorrelationId).SetValidator(new CorrelationIdValidator());
        RuleFor(x => x.ConversationId).SetValidator(new ConversationIdValidator());
        RuleFor(x => x.SagaId).SetValidator(new SagaIdValidator());
        RuleFor(x => x.SentAt).SetValidator(new SentAtValidator());
        RuleFor(x => x.DeliverBy).SetValidator(new DeliverByValidator());
        RuleFor(x => x.Headers).SetValidator(new HeadersValidator());
        RuleFor(x => x.ContentType).SetValidator(new ContentTypeValidator());
        RuleFor(x => x.Attempts).SetValidator(new AttemptsValidator());
        RuleFor(x => x.Data).SetValidator(new EnvelopeDataValidator());

        RuleFor(x => x)
            .Must(envelope => envelope.DeliverBy.Value!.Value >= envelope.SentAt.Value)
            .When(envelope => envelope.DeliverBy.Value.HasValue)
            .WithMessage("DeliverBy must be greater than or equal to SentAt.");

        RuleFor(x => x.Data)
            .Must((_, data) => data.Value.Length > 0)
            .When(envelope => envelope.ContentType.Value.Length > 0)
            .WithMessage("EnvelopeData must not be empty when ContentType is set.");

        RuleFor(x => x.ContentType.Value)
            .NotEmpty()
            .When(envelope => envelope.Data.Value.Length > 0)
            .WithMessage("ContentType must be set when EnvelopeData is present.");
    }
}
