using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Discovery.Validators;
using Cabbie.Application.Execution;
using Cabbie.Application.Mediator;
using Cabbie.Application.Middleware;
using Cabbie.Application.Persistence;
using Cabbie.Application.Routing;
using Cabbie.Application.Sagas;
using Cabbie.Application.Scheduling;
using Cabbie.Application.Serialization;
using Cabbie.Application.Tracking;
using Cabbie.Application.Transports;
using Cabbie.Domain.Sagas;
using Cabbie.Infrastructure.Hosting;
using Cabbie.Infrastructure.LocalQueues;
using Cabbie.Infrastructure.Persistence;
using Cabbie.Infrastructure.Sagas;
using Cabbie.Infrastructure.Scheduling;
using Cabbie.Infrastructure.Serialization;
using Cabbie.Infrastructure.Transports;

namespace Cabbie;

public static class CabbieHostBuilderExtensions
{
    public static IHostApplicationBuilder UseCabbie(
        this IHostApplicationBuilder builder,
        Action<CabbieOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCabbie(configure);
        return builder;
    }

    /// <summary>
    /// Registers Cabbie. Handler assemblies are scanned here, so an invalid handler signature
    /// throws now, and every instance handler is registered scoped: the Executor creates one
    /// scope per handler invocation and resolves the handler and its method services from it.
    /// Handlers added to the catalog later (not scanned here) are built with ActivatorUtilities.
    /// </summary>
    public static IServiceCollection AddCabbie(
        this IServiceCollection services,
        Action<CabbieOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new CabbieOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        var scanned = new HandlerCatalog();
        foreach (Assembly assembly in options.HandlerAssemblies)
        {
            scanned.Scan(assembly);
        }

        foreach (Type handlerType in scanned.Handlers
            .Where(handler => !handler.IsStatic)
            .Select(handler => handler.HandlerType)
            .Where(type => !type.IsAbstract && !type.IsAssignableTo(typeof(Saga)))
            .Distinct())
        {
            services.TryAddScoped(handlerType);
        }

        services.AddSingleton<HandlerCatalog>(_ =>
        {
            FluentValidation.Results.ValidationResult validation = new HandlerCatalogValidator().Validate(scanned);
            if (!validation.IsValid)
            {
                throw new ValidationException(validation.Errors);
            }

            return scanned;
        });
        services.AddSingleton(options.Policies);
        services.AddSingleton<IOutboxStore>(
            services => services.GetRequiredService<IMessageStore>().Outbox);
        services.AddSingleton<TransactionalOutboxMiddleware>();
        services.AddSingleton<MiddlewareCatalog>(services =>
        {
            var catalog = new MiddlewareCatalog();
            if (options.EnableTransactionalOutbox)
            {
                catalog.Register(MiddlewareLayer.Inner, services.GetRequiredService<TransactionalOutboxMiddleware>());
            }

            return catalog;
        });
        services.AddSingleton(options.Routing);
        services.AddSingleton<ISagaStore, InMemorySagaStore>();
        services.AddSingleton<IScheduledEnvelopeHold, InMemoryScheduledEnvelopeHold>();
        services.AddSingleton<IHandlerAttemptObserver, AttemptObserverHub>();
        services.AddSingleton<ISerializer, JsonSerializer>();
        services.AddSingleton<IMessageStore, InMemoryMessageStore>();
        services.AddSingleton<IDeadLetterStore>(
            services => services.GetRequiredService<IMessageStore>().DeadLetter);
        services.AddSingleton<IErrorQueue>(services => new DeadLetterQueueAdapter(
            services.GetRequiredService<IDeadLetterStore>()));
        services.AddSingleton<Executor>(services => new Executor(
            services.GetRequiredService<ErrorPolicyCatalog>(),
            errorQueue: services.GetService<IErrorQueue>(),
            missingHandler: services.GetService<IMissingHandler>(),
            middleware: services.GetRequiredService<MiddlewareCatalog>(),
            scheduled: services.GetRequiredService<IScheduledEnvelopeHold>(),
            attempts: services.GetRequiredService<IHandlerAttemptObserver>(),
            enqueuer: () => services.GetRequiredService<IPublishEnqueuer>(),
            scopes: services.GetRequiredService<IServiceScopeFactory>()));
        services.AddSingleton<MessageDelivery>(services => new MessageDelivery(
            services.GetRequiredService<HandlerCatalog>(),
            services.GetRequiredService<Executor>(),
            hold: services.GetRequiredService<IScheduledEnvelopeHold>(),
            routing: services.GetRequiredService<RoutingCatalog>(),
            transport: () => services.GetRequiredService<ITransport>()));
        services.AddSingleton<LocalQueueCatalog>(services => new LocalQueueCatalog(
            (envelope, cancellationToken) => services.GetRequiredService<Mediator>()
                .DispatchHandlers(envelope, scheduled: true, cancellationToken)));
        services.AddSingleton<IPublishEnqueuer>(
            services => services.GetRequiredService<LocalQueueCatalog>());
        services.AddSingleton<ITransport>(services => new LocalTransport(
            services.GetRequiredService<IPublishEnqueuer>(),
            (envelope, cancellationToken) => services.GetRequiredService<Mediator>()
                .DispatchHandlers(envelope, scheduled: true, cancellationToken)));
        services.AddSingleton<Mediator>(services => new Mediator(
            services.GetRequiredService<HandlerCatalog>(),
            executor: services.GetRequiredService<Executor>(),
            hold: services.GetRequiredService<IScheduledEnvelopeHold>(),
            sagas: services.GetRequiredService<ISagaStore>(),
            routing: services.GetRequiredService<RoutingCatalog>(),
            enqueuer: services.GetRequiredService<IPublishEnqueuer>(),
            attempts: services.GetRequiredService<IHandlerAttemptObserver>()));
        services.AddSingleton<IMessageBus>(services => services.GetRequiredService<Mediator>());
        services.AddSingleton<CabbieHostedService>();
        services.AddSingleton<IHostedService>(
            services => services.GetRequiredService<CabbieHostedService>());
        return services;
    }
}