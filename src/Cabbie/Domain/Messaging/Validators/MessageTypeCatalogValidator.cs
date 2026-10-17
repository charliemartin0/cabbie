using System.Reflection;
using Cabbie.Domain.Messaging.ValueObjects;
using FluentValidation;

namespace Cabbie.Domain.Messaging.Validators;

/// <summary>
/// Per-registration rules live on MessageTypeValidator and MessageIdentityAttributeValidator.
/// This type composes them and checks that wire names are unique.
/// </summary>
public sealed class MessageTypeCatalogValidator : AbstractValidator<MessageTypeCatalog>
{
    public MessageTypeCatalogValidator()
    {
        RuleForEach(x => x.Registrations)
            .ChildRules(registration =>
            {
                registration.RuleFor(r => r.Type)
                    .NotNull()
                    .WithMessage("Registered type must not be null.");

                registration.RuleFor(r => r.Name)
                    .SetValidator(new MessageTypeValidator());

                registration.When(
                    r => r.Type.GetCustomAttribute<MessageIdentityAttribute>(inherit: false) is not null,
                    () =>
                    {
                        registration.RuleFor(r => r.Type.GetCustomAttribute<MessageIdentityAttribute>(inherit: false)!)
                            .SetValidator(new MessageIdentityAttributeValidator());
                    });
            });

        RuleFor(x => x.Registrations)
            .Must(HaveUniqueWireNames)
            .WithMessage("Wire names must be unique. Two CLR types cannot share the same alias or FullName.");
    }

    private static bool HaveUniqueWireNames(IReadOnlyList<(Type Type, MessageType Name)> registrations)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, name) in registrations)
        {
            if (!names.Add(name.Value))
            {
                return false;
            }
        }

        return true;
    }
}
