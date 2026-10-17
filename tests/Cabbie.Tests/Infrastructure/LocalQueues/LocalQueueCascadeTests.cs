using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Infrastructure.LocalQueues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Tests.Infrastructure.LocalQueues;

/// <summary>
/// Prove-with for cascade propagation through the local queue: a handler's return value
/// must be re-enqueued (not dropped) and its handler must run.
/// </summary>
public sealed class LocalQueueCascadeTests
{
    [Fact]
    public async Task local_queue_handler_return_value_is_enqueued_as_cascade()
    {
        CascadeTargetHandler.Reset();
        using IHost host = await StartedHostAsync(typeof(CascadeSourceHandler), typeof(CascadeTargetHandler));
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new CascadeSourceMessage(7));

        Assert.Equal(7, await CascadeTargetHandler.Handled.WaitAsync(TimeSpan.FromSeconds(5)));
        await host.StopAsync();
    }

    private static async Task<IHost> StartedHostAsync(params Type[] handlerTypes)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie();
        IHost host = builder.Build();
        HandlerCatalog catalog = host.Services.GetRequiredService<HandlerCatalog>();
        foreach (Type handlerType in handlerTypes)
        {
            catalog.Scan(handlerType);
        }

        await host.StartAsync();
        return host;
    }
}

public sealed record CascadeSourceMessage(int OrderId);

public sealed record CascadeTargetMessage(int OrderId);

public sealed class CascadeSourceHandler
{
    public CascadeTargetMessage Handle(CascadeSourceMessage message) => new(message.OrderId);
}

public sealed class CascadeTargetHandler
{
    private static TaskCompletionSource<int> _handled = New();

    public static Task<int> Handled => _handled.Task;

    public static void Reset() => _handled = New();

    public void Handle(CascadeTargetMessage message) => _handled.TrySetResult(message.OrderId);

    private static TaskCompletionSource<int> New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}