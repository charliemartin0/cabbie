using Cabbie.Domain.Messaging.Validators;
using FluentValidation;

namespace Cabbie.Application.Discovery.Validators;

/// <summary>
/// Catalog rules compose MessageTypeCatalogValidator. Discovery does not throw on a missing handler.
/// </summary>
public sealed class HandlerCatalogValidator : AbstractValidator<HandlerCatalog>
{
    public HandlerCatalogValidator()
    {
        RuleFor(x => x.MessageTypes).SetValidator(new MessageTypeCatalogValidator());
    }
}
