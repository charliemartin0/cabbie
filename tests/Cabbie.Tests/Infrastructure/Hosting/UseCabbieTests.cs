using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Infrastructure.LocalQueues;
using Cabbie.Tests.Handlers;

namespace Cabbie.Tests.Infrastructure.Hosting;

/// <summary>
/// Prove-with for the DX slice: UseCabbie must actually consume the options —
/// scan the configured handler assemblies and apply the fluent routing registrations
/// — so Cabbie works as a library through its public surface.
/// </summary>
public sealed class UseCabbieTests
{
    [Fact]
    public void use_cabbie_scans_handler_assemblies()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(ScannedCommandHandler).Assembly));
        using IHost host = builder.Build();

        HandlerCatalog catalog = host.Services.GetRequiredService<HandlerCatalog>();

        HandlerLookup lookup = catalog.Lookup(typeof(ScannedCommand));
        FoundHandlers found = Assert.IsType<FoundHandlers>(lookup);
        Assert.Contains(found.Handlers, handler => handler.HandlerType == typeof(ScannedCommandHandler));
    }

    [Fact]
    public async Task host_publish_routes_charge_payment_to_payments_queue()
    {
        ChargePaymentHandler.Reset();
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options =>
        {
            options.HandlerAssemblies.Add(typeof(ChargePaymentHandler).Assembly);
            options.PublishMessage<ChargePayment>().ToLocalQueue("payments");
        });
        using IHost host = builder.Build();
        // Resolve the bus before StartAsync: catalog scanning completes here, before any dispatch.
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
        await host.StartAsync();
        try
        {
            await bus.PublishAsync(new ChargePayment(7));

            ChargePayment handled = await ChargePaymentHandler.Handled.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(7, handled.OrderId);

            // The fluent route must have sent it to the "payments" agent, not the type-name fallback.
            LocalQueueCatalog queues = host.Services.GetRequiredService<LocalQueueCatalog>();
            Assert.Contains(queues.All, agent => agent.Name == "local://payments/");
        }
        finally
        {
            await host.StopAsync();
        }
    }
}