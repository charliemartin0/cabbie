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

namespace Cabbie;

public static class CabbieHostBuilderExtensions
{
    public static IHostApplicationBuilder UseCabbie(
        this IHostApplicationBuilder builder,
        Action<CabbieOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new CabbieOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<HandlerCatalog>(services =>
        {
            var catalog = new HandlerCatalog();
            foreach (Assembly assembly in options.HandlerAssemblies)
            {
                catalog.Scan(assembly);
            }

            FluentValidation.Results.ValidationResult validation = new HandlerCatalogValidator().Validate(catalog);
            if (!validation.IsValid)
            {
                throw new ValidationException(validation.Errors);
            }

            return catalog;
        });
        builder.Services.AddSingleton(options.Policies);
        builder.Services.AddSingleton<IOutboxStore>(
            services => services.GetRequiredService<IMessageStore>().Outbox);
        builder.Services.AddSingleton<TransactionalOutboxMiddleware>();
        builder.Services.AddSingleton<MiddlewareCatalog>(services =>
        {
            var catalog = new MiddlewareCatalog();
            if (options.EnableTransactionalOutbox)
            {
                catalog.Register(MiddlewareLayer.Inner, services.GetRequiredService<TransactionalOutboxMiddleware>());
            }

            return catalog;
        });
        builder.Services.AddSingleton(options.Routing);
        builder.Services.AddSingleton<ISagaStore, InMemorySagaStore>();
        builder.Services.AddSingleton<IScheduledEnvelopeHold, InMemoryScheduledEnvelopeHold>();
        builder.Services.AddSingleton<IHandlerAttemptObserver, AttemptObserverHub>();
        builder.Services.AddSingleton<ISerializer, JsonSerializer>();
        builder.Services.AddSingleton<IMessageStore, InMemoryMessageStore>();
        builder.Services.AddSingleton<IDeadLetterStore>(
            services => services.GetRequiredService<IMessageStore>().DeadLetter);
        builder.Services.AddSingleton<IErrorQueue>(services => new DeadLetterQueueAdapter(
            services.GetRequiredService<IDeadLetterStore>()));
        builder.Services.AddSingleton<Executor>(services => new Executor(
            services.GetRequiredService<ErrorPolicyCatalog>(),
            errorQueue: services.GetService<IErrorQueue>(),
            missingHandler: services.GetService<IMissingHandler>(),
            middleware: services.GetRequiredService<MiddlewareCatalog>(),
            scheduled: services.GetRequiredService<IScheduledEnvelopeHold>(),
            attempts: services.GetRequiredService<IHandlerAttemptObserver>(),
            enqueuer: () => services.GetRequiredService<IPublishEnqueuer>()));
        builder.Services.AddSingleton<MessageDelivery>(services => new MessageDelivery(
            services.GetRequiredService<HandlerCatalog>(),
            services.GetRequiredService<Executor>(),
            hold: services.GetRequiredService<IScheduledEnvelopeHold>(),
            routing: services.GetRequiredService<RoutingCatalog>(),
            transport: () => services.GetRequiredService<ITransport>()));
        builder.Services.AddSingleton<LocalQueueCatalog>(services => new LocalQueueCatalog(
            (envelope, cancellationToken) => services.GetRequiredService<Mediator>()
                .DispatchHandlers(envelope, scheduled: true, cancellationToken)));
        builder.Services.AddSingleton<IPublishEnqueuer>(
            services => services.GetRequiredService<LocalQueueCatalog>());
        builder.Services.AddSingleton<ITransport>(services => new LocalTransport(
            services.GetRequiredService<IPublishEnqueuer>(),
            (envelope, cancellationToken) => services.GetRequiredService<Mediator>()
                .DispatchHandlers(envelope, scheduled: true, cancellationToken)));
        builder.Services.AddSingleton<Mediator>(services => new Mediator(
            services.GetRequiredService<HandlerCatalog>(),
            executor: services.GetRequiredService<Executor>(),
            hold: services.GetRequiredService<IScheduledEnvelopeHold>(),
            sagas: services.GetRequiredService<ISagaStore>(),
            routing: services.GetRequiredService<RoutingCatalog>(),
            enqueuer: services.GetRequiredService<IPublishEnqueuer>(),
            attempts: services.GetRequiredService<IHandlerAttemptObserver>()));
        builder.Services.AddSingleton<IMessageBus>(services => services.GetRequiredService<Mediator>());
        builder.Services.AddSingleton<CabbieHostedService>();
        builder.Services.AddSingleton<IHostedService>(
            services => services.GetRequiredService<CabbieHostedService>());
        return builder;
    }
}