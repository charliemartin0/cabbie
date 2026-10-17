using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Infrastructure.Hosting;

/// <summary>
/// Handler types AddCabbie registered by type (the scanned instance handlers it did not find
/// already registered).
/// </summary>
internal sealed record CabbieHandlerRegistrations(IReadOnlySet<Type> Types);

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
        HandlerDependencyValidator.Validate(catalog, isService, registrations.Types);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
