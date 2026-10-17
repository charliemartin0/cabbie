using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Routing;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Infrastructure.LocalQueues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Tests.Infrastructure.LocalQueues;

public sealed class LocalQueueTests
{
    [Fact]
    public async Task publish_async_returns_before_handle_runs()
    {
        GatedHandler.Reset();
        using IHost host = await StartedHostAsync(typeof(GatedHandler));
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

        Task publish = bus.PublishAsync(new GatedMessage(1));
        await publish.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Assert.False(GatedHandler.Completed.IsCompleted);
        }
        finally
        {
            GatedHandler.Allow();
        }

        await GatedHandler.Completed.WaitAsync(TimeSpan.FromSeconds(5));
        await host.StopAsync();
    }

    [Fact]
    public async Task publish_async_dispatches_through_executor_to_discovered_handler()
    {
        PaymentRecordingHandler.Reset();
        using IHost host = await StartedHostAsync(typeof(PaymentRecordingHandler));
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new PaymentMessage(42));

        (int Id, string Destination) recorded =
            await PaymentRecordingHandler.Recorded.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, recorded.Id);
        Assert.Equal("local://payments/", recorded.Destination);
        await host.StopAsync();
    }

    [Fact]
    public async Task publish_async_routes_via_local_queue_attribute_to_named_queue()
    {
        AttributedRecordingHandler.Reset();
        using IHost host = await StartedHostAsync(typeof(AttributedRecordingHandler));
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new AttributedPaymentMessage(1));

        string destination = await AttributedRecordingHandler.Recorded.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("local://payments/", destination);
        await host.StopAsync();
    }

    [Fact]
    public async Task publish_async_with_no_route_falls_back_to_lowercased_type_name()
    {
        NoRouteRecordingHandler.Reset();
        using IHost host = await StartedHostAsync(typeof(NoRouteRecordingHandler));
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new NoRouteMessage(1));

        string destination = await NoRouteRecordingHandler.Recorded.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("local://noroutemessage/", destination);
        await host.StopAsync();
    }

    [Fact]
    public async Task host_stops_drain_in_flight_local_queue_work()
    {
        SlowHandler.Reset();
        using IHost host = await StartedHostAsync(typeof(SlowHandler));
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

        for (int i = 0; i < 5; i++)
        {
            await bus.PublishAsync(new SlowMessage(i));
        }

        await host.StopAsync();

        Assert.Equal(5, SlowHandler.Count);
    }

    [Fact]
    public async Task local_queue_agent_pause_blocks_dispatch_until_resume()
    {
        PausableHandler.Reset();
        using IHost host = await StartedHostAsync(typeof(PausableHandler));
        LocalQueueCatalog catalog = host.Services.GetRequiredService<LocalQueueCatalog>();
        LocalQueueAgent agent = catalog.For(new Destination(new Uri("local://pausable/")));
        agent.Pause();
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new PausableMessage(1));
        await Task.Delay(150);

        Assert.Equal(0, PausableHandler.Count);

        agent.Resume();
        await PausableHandler.Handled.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, PausableHandler.Count);
        await host.StopAsync();
    }

    [Fact]
    public async Task local_queue_catalog_creates_separate_agents_per_destination()
    {
        using IHost host = await StartedHostAsync();
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
        LocalQueueCatalog catalog = host.Services.GetRequiredService<LocalQueueCatalog>();

        await bus.PublishAsync(new PaymentsMessage(1));
        await bus.PublishAsync(new ShippingMessage(1));

        LocalQueueAgent[] agents = [.. catalog.All];
        Assert.Equal(2, agents.Length);
        LocalQueueAgent payments = Assert.Single(agents, agent => agent.Name == "local://payments/");
        LocalQueueAgent shipping = Assert.Single(agents, agent => agent.Name == "local://shipping/");
        Assert.NotSame(payments, shipping);
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

public sealed record GatedMessage(int OrderId);

public sealed class GatedHandler
{
    private static TaskCompletionSource _release = New();
    private static TaskCompletionSource _completed = New();

    public static Task Completed => _completed.Task;

    public static void Reset()
    {
        _release = New();
        _completed = New();
    }

    public static void Allow() => _release.TrySetResult();

    public async Task Handle(GatedMessage message)
    {
        await _release.Task;
        _completed.TrySetResult();
    }

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

[LocalQueue("payments")]
public sealed record PaymentMessage(int OrderId);

public sealed class PaymentRecordingHandler
{
    private static TaskCompletionSource<(int Id, string Destination)> _recorded = New();

    public static Task<(int Id, string Destination)> Recorded => _recorded.Task;

    public static void Reset() => _recorded = New();

    public void Handle(PaymentMessage message, Envelope envelope) =>
        _recorded.TrySetResult((message.OrderId, envelope.Destination.Value.ToString()));

    private static TaskCompletionSource<(int, string)> New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

[LocalQueue("payments")]
public sealed record AttributedPaymentMessage(int OrderId);

public sealed class AttributedRecordingHandler
{
    private static TaskCompletionSource<string> _recorded = New();

    public static Task<string> Recorded => _recorded.Task;

    public static void Reset() => _recorded = New();

    public void Handle(AttributedPaymentMessage message, Envelope envelope) =>
        _recorded.TrySetResult(envelope.Destination.Value.ToString());

    private static TaskCompletionSource<string> New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed record NoRouteMessage(int OrderId);

public sealed class NoRouteRecordingHandler
{
    private static TaskCompletionSource<string> _recorded = New();

    public static Task<string> Recorded => _recorded.Task;

    public static void Reset() => _recorded = New();

    public void Handle(NoRouteMessage message, Envelope envelope) =>
        _recorded.TrySetResult(envelope.Destination.Value.ToString());

    private static TaskCompletionSource<string> New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed record SlowMessage(int OrderId);

public sealed class SlowHandler
{
    private static int _count;

    public static int Count => Volatile.Read(ref _count);

    public static void Reset() => Interlocked.Exchange(ref _count, 0);

    public async Task Handle(SlowMessage message)
    {
        await Task.Delay(20);
        Interlocked.Increment(ref _count);
    }
}

[LocalQueue("pausable")]
public sealed record PausableMessage(int OrderId);

public sealed class PausableHandler
{
    private static int _count;
    private static TaskCompletionSource _handled = New();

    public static int Count => Volatile.Read(ref _count);

    public static Task Handled => _handled.Task;

    public static void Reset()
    {
        Interlocked.Exchange(ref _count, 0);
        _handled = New();
    }

    public void Handle(PausableMessage message)
    {
        Interlocked.Increment(ref _count);
        _handled.TrySetResult();
    }

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

[LocalQueue("payments")]
public sealed record PaymentsMessage(int OrderId);

[LocalQueue("shipping")]
public sealed record ShippingMessage(int OrderId);
