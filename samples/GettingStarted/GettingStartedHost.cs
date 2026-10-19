using Cabbie;
using Cabbie.Application.Bus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GettingStarted;

public static class GettingStartedHost
{
    /// <summary>
    /// Builds an in-memory host, sends PlaceOrder(1), waits for its OrderPlaced cascade, and
    /// returns the order ids the cascade handler saw.
    /// </summary>
    public static async Task<IReadOnlyList<int>> RunAsync(CancellationToken cancellationToken = default)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<OrderLog>();
        builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(GettingStartedHost).Assembly));

        using IHost host = builder.Build();
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
        await bus.InvokeTrackedAsync(new PlaceOrder(1), cancellationToken);

        return host.Services.GetRequiredService<OrderLog>().Placed;
    }
}
