using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Tests.Application.Execution;
using Cabbie.Tests.FactoryHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Tests.Infrastructure.Hosting;

/// <summary>
/// Prove-with for fail-fast DI: a handler that needs a service the container does not know
/// stops host startup, naming the handler, the parameter and the service type.
/// </summary>
public sealed class HandlerDependencyStartupTests
{
    [Fact]
    public async Task startup_fails_when_a_handler_method_parameter_is_not_registered()
    {
        using IHost host = BuildHostWith(typeof(NeedsMethodServiceHandler));

        HandlerDependencyMissing missing = await Assert.ThrowsAsync<HandlerDependencyMissing>(() => host.StartAsync());

        Assert.Equal(typeof(NeedsMethodServiceHandler), missing.HandlerType);
        Assert.Equal(typeof(IUnregisteredService), missing.ServiceType);
        Assert.Contains("'service'", missing.Message);
        Assert.Contains(typeof(NeedsMethodServiceHandler).FullName!, missing.Message);
    }

    [Fact]
    public async Task startup_fails_when_a_handler_constructor_parameter_is_not_registered()
    {
        using IHost host = BuildHostWith(typeof(NeedsCtorServiceHandler));

        HandlerDependencyMissing missing = await Assert.ThrowsAsync<HandlerDependencyMissing>(() => host.StartAsync());

        Assert.Equal(typeof(NeedsCtorServiceHandler), missing.HandlerType);
        Assert.Equal(typeof(IUnregisteredService), missing.ServiceType);
        Assert.Contains("constructor parameter 'service'", missing.Message);
    }

    [Fact]
    public async Task startup_accepts_a_handler_the_application_registered_itself()
    {
        // string is not a service: only the factory registration can build this handler.
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie();
        builder.Services.AddScoped(_ => new RegisteredOnlyHandler("from-container"));
        using IHost host = builder.Build();
        host.Services.GetRequiredService<HandlerCatalog>().Scan(typeof(RegisteredOnlyHandler));

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public void constructors_are_checked_for_handlers_cabbie_registered_but_not_for_the_applications_own()
    {
        var services = new ServiceCollection();
        services.AddScoped<NeedsCtorServiceHandler>();
        using ServiceProvider provider = services.BuildServiceProvider();
        var isService = provider.GetRequiredService<IServiceProviderIsService>();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(NeedsCtorServiceHandler));

        HandlerDependencyValidator.Validate(catalog, isService, _ => false);
        HandlerDependencyMissing missing = Assert.Throws<HandlerDependencyMissing>(
            () => HandlerDependencyValidator.Validate(catalog, isService, type => type == typeof(NeedsCtorServiceHandler)));

        Assert.Equal(typeof(IUnregisteredService), missing.ServiceType);
    }

    [Fact]
    public async Task handler_registered_with_a_factory_after_add_cabbie_is_not_second_guessed_and_is_the_one_used()
    {
        // FactoryBuiltHandler is scanned by AddCabbie, which registers it by type first; the
        // factory registered afterwards wins, so its string constructor must not fail startup.
        var sink = new List<string>();
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(FactoryBuiltHandler).Assembly));
        builder.Services.AddScoped(_ => new FactoryBuiltHandler("from-factory", sink));
        using IHost host = builder.Build();

        await host.StartAsync();
        try
        {
            await host.Services.GetRequiredService<IMessageBus>().InvokeAsync(new FactoryBuiltMessage());

            Assert.Equal(["from-factory"], sink);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task keyed_registrations_do_not_count_as_the_applications_registration_of_a_handler()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(FactoryBuiltHandler).Assembly));
        builder.Services.AddKeyedScoped("other", (_, _) => new FactoryBuiltHandler("keyed", []));
        using IHost host = builder.Build();

        HandlerDependencyMissing missing = await Assert.ThrowsAsync<HandlerDependencyMissing>(() => host.StartAsync());

        Assert.Equal(typeof(FactoryBuiltHandler), missing.HandlerType);
        Assert.Equal(typeof(string), missing.ServiceType);
    }

    private static IHost BuildHostWith(Type handlerType)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie();
        IHost host = builder.Build();
        host.Services.GetRequiredService<HandlerCatalog>().Scan(handlerType);
        return host;
    }
}
