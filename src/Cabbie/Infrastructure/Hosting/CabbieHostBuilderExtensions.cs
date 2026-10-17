using System.Reflection;
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
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    /// Registers Cabbie. Handler assemblies are scanned and the catalog validated here, so an
    /// invalid handler signature or catalog throws now, and every instance handler not already
    /// registered is registered scoped: the Executor creates one scope per handler invocation and
    /// resolves the handler and its method services from it. A registration the application adds
    /// later for the same type wins, because the container resolves the last one; the startup check
    /// then leaves that handler's constructor alone.
    /// Handlers added to the catalog later (not scanned here) are built with ActivatorUtilities.
    /// </summary>
    public static IServiceCollection AddCabbie(
        this IServiceCollection services,
        Action<CabbieOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new CabbieOptions();
        configure?.Invoke(options);
        if (options.HandlerTimeout is { } handlerTimeout)
        {
            Executor.ValidateHandlerTimeout(handlerTimeout, nameof(CabbieOptions.HandlerTimeout));
        }

        options.Policies.UseDefaultRetryDelays(options.DefaultRetryDelays);

        services.AddSingleton(options);

        var scanned = new HandlerCatalog();
        foreach (Assembly assembly in options.HandlerAssemblies)
        {
            scanned.Scan(assembly);
        }

        FluentValidation.Results.ValidationResult validation = new HandlerCatalogValidator().Validate(scanned);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        var added = new HashSet<ServiceDescriptor>();
        foreach (Type handlerType in scanned.Handlers
            .Where(handler => !handler.IsStatic)
            .Select(handler => handler.HandlerType)
            .Where(type => !type.IsAbstract && !type.IsAssignableTo(typeof(Saga)))
            .Distinct())
        {
            if (!services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == handlerType))
            {
                var descriptor = ServiceDescriptor.Scoped(handlerType, handlerType);
                services.Add(descriptor);
                added.Add(descriptor);
            }
        }

        services.AddSingleton(scanned);
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
            scopes: services.GetRequiredService<IServiceScopeFactory>(),
            handlerTimeout: options.HandlerTimeout,
            logger: services.GetService<ILogger<Executor>>(),
            timeProvider: services.GetService<TimeProvider>()));
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
        services.AddSingleton(new CabbieHandlerRegistrations(services, added));
        services.AddSingleton<IHostedService, HandlerDependencyStartupCheck>();
        services.AddSingleton<CabbieHostedService>();
        services.AddSingleton<IHostedService>(
            services => services.GetRequiredService<CabbieHostedService>());
        return services;
    }
}