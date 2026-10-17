using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Infrastructure.Hosting;

/// <summary>
/// The container registrations AddCabbie added for scanned instance handlers, kept by
/// reference. Whether a handler is "registered by Cabbie" is decided from the finished service
/// collection when the check runs, so a registration the application adds after AddCabbie
/// (a factory, say) takes over: DI resolves the last non-keyed registration of a type.
/// </summary>
internal sealed class CabbieHandlerRegistrations(
    IServiceCollection services,
    IReadOnlySet<ServiceDescriptor> added)
{
    public bool IsRegisteredByCabbie(Type handlerType)
    {
        ServiceDescriptor? effective = services.LastOrDefault(
            descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == handlerType);
        return effective is not null && added.Contains(effective);
    }
}

/// <summary>
/// Fails host startup when a handler needs a service the container does not know. Registered
/// ahead of <see cref="CabbieHostedService"/>, so it runs before any queue agent starts.
/// </summary>
internal sealed class HandlerDependencyStartupCheck(
    HandlerCatalog catalog,
    IServiceProviderIsService isService,
    CabbieHandlerRegistrations registrations) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        HandlerDependencyValidator.Validate(catalog, isService, registrations.IsRegisteredByCabbie);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
