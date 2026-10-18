using Cabbie.Application.Routing;
using Cabbie.Application.Transports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cabbie.EntityFrameworkCore;

public static class EfOutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EfOutbox{TContext}"/> (scoped) and the hosted relay that sends its rows through the
    /// <see cref="TransportRouter"/>. Call after <c>UseCabbie</c>; register the <typeparamref name="TContext"/> yourself.
    /// </summary>
    public static IServiceCollection AddCabbieEfOutbox<TContext>(
        this IServiceCollection services,
        EfOutboxRelayOptions? options = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        EfOutboxRelayOptions resolved = options ?? new EfOutboxRelayOptions();
        services.AddScoped(provider => new EfOutbox<TContext>(
            provider.GetRequiredService<TContext>(),
            provider.GetRequiredService<EnvelopeCodec>(),
            provider.GetRequiredService<RoutingCatalog>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton(provider => new OutboxRelay<TContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TransportRouter>(),
            provider.GetRequiredService<EnvelopeCodec>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            resolved,
            provider.GetService<ILogger<OutboxRelay<TContext>>>()));
        services.AddSingleton<IHostedService>(provider => new OutboxRelayService<TContext>(
            provider.GetRequiredService<OutboxRelay<TContext>>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            resolved,
            provider.GetService<ILogger<OutboxRelayService<TContext>>>()));
        return services;
    }
}
