using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Tests.Application.Execution;
using Cabbie.Tests.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Tests.Infrastructure.Hosting;

/// <summary>
/// Prove-with for AddCabbie: scanned handlers are registered scoped so the per-message
/// scope owns them, sagas are not, and the whole path resolves constructor services.
/// </summary>
public sealed class AddCabbieTests
{
    [Fact]
    public void add_cabbie_registers_scanned_handlers_as_scoped()
    {
        var services = new ServiceCollection();
        services.AddCabbie(options => options.HandlerAssemblies.Add(typeof(ScannedCommandHandler).Assembly));

        ServiceDescriptor handler = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(ScannedCommandHandler));
        Assert.Equal(ServiceLifetime.Scoped, handler.Lifetime);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(QueueSaga));
    }

    [Fact]
    public async Task host_resolves_constructor_injected_handler_end_to_end()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie();
        builder.Services.AddScoped<IGreeter>(_ => new Greeter("hello from DI"));
        var log = new GreetingLog();
        builder.Services.AddSingleton(log);
        using IHost host = builder.Build();
        host.Services.GetRequiredService<HandlerCatalog>().Scan(typeof(CtorGreetingHandler));
        await host.StartAsync();
        try
        {
            IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

            await bus.InvokeAsync(new CtorGreetingMessage());

            Assert.Equal(["hello from DI"], log.Greetings);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}

public sealed record CtorGreetingMessage;

public sealed class GreetingLog
{
    public List<string> Greetings { get; } = [];
}

public sealed class CtorGreetingHandler(IGreeter greeter, GreetingLog log)
{
    public void Handle(CtorGreetingMessage message) => log.Greetings.Add(greeter.Greet());
}
