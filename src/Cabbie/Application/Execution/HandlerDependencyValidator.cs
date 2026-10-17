using Cabbie.Application.Discovery;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Sagas;
using Microsoft.Extensions.DependencyInjection;

namespace Cabbie.Application.Execution;

/// <summary>
/// Startup check: every handler method parameter that is not Envelope or CancellationToken must
/// be a service the container knows, and so must every constructor parameter of a handler
/// Cabbie builds from the container. Fails with the handler, parameter and service type instead
/// of waiting for the first message.
/// </summary>
public static class HandlerDependencyValidator
{
    /// <param name="catalog">The scanned handlers.</param>
    /// <param name="isService">The container's registration probe.</param>
    /// <param name="registeredByCabbie">
    /// Handler types AddCabbie registered by type. Their constructors are checked, and so are the
    /// constructors of handlers with no registration at all (built by ActivatorUtilities). A handler
    /// the application registered itself (often through a factory) is not second-guessed.
    /// </param>
    public static void Validate(
        HandlerCatalog catalog,
        IServiceProviderIsService isService,
        IReadOnlySet<Type> registeredByCabbie)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(isService);
        ArgumentNullException.ThrowIfNull(registeredByCabbie);

        foreach (DiscoveredHandler handler in catalog.Handlers)
        {
            foreach (var slot in handler.InjectionSlots)
            {
                if (slot.ParameterType != typeof(Envelope)
                    && slot.ParameterType != typeof(CancellationToken)
                    && !isService.IsService(slot.ParameterType))
                {
                    throw new HandlerDependencyMissing(slot, handler.HandlerType);
                }
            }

            bool builtFromContainer = !handler.IsStatic
                && handler.ResolveTarget is null
                && !handler.HandlerType.IsAssignableTo(typeof(Saga))
                && (registeredByCabbie.Contains(handler.HandlerType) || !isService.IsService(handler.HandlerType));
            if (builtFromContainer
                && HandlerServices.FindUnresolvable(isService, handler.HandlerType) is { } parameter)
            {
                throw new HandlerDependencyMissing(parameter, handler.HandlerType);
            }
        }
    }
}
