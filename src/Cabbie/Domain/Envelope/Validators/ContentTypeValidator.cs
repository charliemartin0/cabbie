using FluentValidation;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Domain.Envelope.Validators;

public sealed class ContentTypeValidator : AbstractValidator<ContentType>
{
    public ContentTypeValidator()
    {
        RuleFor(x => x.Value)
            .NotNull()
            .WithMessage("ContentType must not be null. Use an empty string before serialization.")
            .Must(value => value.Length == 0 || !string.IsNullOrWhiteSpace(value))
            .WithMessage("ContentType must be empty (not serialized yet) or a non-whitespace media type.");
    }
}
