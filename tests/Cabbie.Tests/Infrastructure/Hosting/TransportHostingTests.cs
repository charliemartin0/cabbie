using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Infrastructure.Hosting;
using Cabbie.Infrastructure.LocalQueues;
using Cabbie.Tests.Application.Transports;
using Cabbie.Tests.Domain.Envelope;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Tests.Infrastructure.Hosting;

public sealed class TransportHostingTests
{
    [Fact]
    public async Task message_routed_to_a_registered_scheme_goes_to_that_transport_not_a_local_queue()
    {
        RoutedOrderHandler.Handled = false;
        var transport = new RecordingTransport("test");
        using IHost host = Build<RoutedOrderHandler>(
            transport,
            options => options.PublishMessage<RoutedOrder>().To("test://orders"));
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new RoutedOrder(1));
        await Task.Delay(100);

        Envelope sent = Assert.Single(transport.Sent);
        Assert.Equal(new Uri("test://orders/"), sent.Destination.Value);
        Assert.IsType<RoutedOrder>(sent.Message.Value);
        Assert.False(RoutedOrderHandler.Handled);
        Assert.DoesNotContain(
            host.Services.GetRequiredService<LocalQueueCatalog>().All,
            agent => agent.Name.StartsWith("test:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task host_start_fails_for_a_route_whose_scheme_has_no_transport()
    {
        using IHost host = Build<RoutedOrderHandler>(
            transport: null,
            options => options.PublishMessage<RoutedOrder>().To("sqs://orders"));

        UnknownTransportScheme error = await Assert.ThrowsAsync<UnknownTransportScheme>(() => host.StartAsync());

        Assert.Contains("'sqs'", error.Message, StringComparison.Ordinal);
        Assert.Contains("sqs://orders/", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task host_start_fails_for_a_listen_endpoint_whose_scheme_has_no_transport()
    {
        using IHost host = Build<RoutedOrderHandler>(
            transport: null,
            options => options.ListenTo("sqs://inbox"));

        UnknownTransportScheme error = await Assert.ThrowsAsync<UnknownTransportScheme>(() => host.StartAsync());

        Assert.Contains("'sqs'", error.Message, StringComparison.Ordinal);
        Assert.Contains("sqs://inbox/", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task host_starts_listen_to_endpoints_and_stops_them_on_shutdown()
    {
        var transport = new RecordingTransport("test");
        using IHost host = Build<RoutedOrderHandler>(transport, options => options.ListenTo("test://inbox"));

        await host.StartAsync();
        RecordingListener listener = Assert.Single(transport.Listeners);
        Assert.Equal(new Uri("test://inbox/"), listener.Address);
        Assert.True(listener.Started);
        Assert.False(listener.Stopped);

        await host.StopAsync();

        Assert.True(listener.Stopped);
    }

    [Fact]
    public async Task envelope_pushed_by_a_listener_runs_the_handler_and_is_acked()
    {
        InboxOrderHandler.Reset();
        var transport = new RecordingTransport("test");
        using IHost host = Build<InboxOrderHandler>(transport, options => options.ListenTo("test://inbox"));
        await host.StartAsync();
        RecordingListener listener = Assert.Single(transport.Listeners);
        Envelope envelope = EnvelopeFactory.Create(
            message: new Message(new InboxOrder(5)),
            messageType: MessageTypeNaming.For(typeof(InboxOrder)),
            destination: new Destination(new Uri("test://inbox/")));

        ReceiveOutcome outcome = await listener.PushAsync(envelope);

        Assert.Equal(ReceiveOutcome.Ack, outcome);
        Assert.Equal(5, await InboxOrderHandler.Handled.WaitAsync(TimeSpan.FromSeconds(5)));
        await host.StopAsync();
    }

    [Fact]
    public async Task local_listen_to_endpoint_receives_messages_published_to_it()
    {
        ListenedOrderHandler.Reset();
        using IHost host = Build<ListenedOrderHandler>(
            transport: null,
            options =>
            {
                options.ListenTo("local://orders");
                options.PublishMessage<ListenedOrder>().To("local://orders");
            });
        await host.StartAsync();
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new ListenedOrder(9));

        (int OrderId, Uri Destination) handled = await ListenedOrderHandler.Handled.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(9, handled.OrderId);
        Assert.Equal(new Uri("local://orders/"), handled.Destination);
        await host.StopAsync();
    }

    [Fact]
    public async Task host_stop_disposes_the_senders_the_router_created()
    {
        var transport = new RecordingTransport("test");
        using IHost host = Build<RoutedOrderHandler>(
            transport,
            options => options.PublishMessage<RoutedOrder>().To("test://orders"));
        await host.StartAsync();
        await host.Services.GetRequiredService<IMessageBus>().PublishAsync(new RoutedOrder(1));
        RecordingSender sender = Assert.Single(transport.Senders.Values);
        Assert.Equal(0, sender.Disposals);

        await host.StopAsync();

        Assert.Equal(1, sender.Disposals);
    }

    [Fact]
    public async Task host_start_failure_in_a_later_listener_stops_the_listeners_already_started()
    {
        var transport = new RecordingTransport("test");
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options =>
        {
            options.ListenTo("test://first");
            options.ListenTo("boom://second");
        });
        builder.Services.AddSingleton<ITransport>(transport);
        builder.Services.AddSingleton<ITransport, ThrowingListenerTransport>();
        using IHost host = builder.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.True(Assert.Single(transport.Listeners).Stopped);
    }

    private sealed class ThrowingListenerTransport : ITransport
    {
        public string Scheme => "boom";

        public ISender CreateSender(Uri address) => new RecordingSender();

        public IListener CreateListener(Uri address, IReceiver receiver) =>
            throw new InvalidOperationException("cannot listen");
    }

    private static IHost Build<THandler>(RecordingTransport? transport, Action<CabbieOptions> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(configure);
        if (transport is not null)
        {
            builder.Services.AddSingleton<ITransport>(transport);
        }

        IHost host = builder.Build();
        host.Services.GetRequiredService<HandlerCatalog>().Scan(typeof(THandler));
        return host;
    }
}

public sealed record RoutedOrder(int OrderId);

public sealed class RoutedOrderHandler
{
    public static bool Handled { get; set; }

    public void Handle(RoutedOrder message) => Handled = true;
}

public sealed record InboxOrder(int OrderId);

public sealed class InboxOrderHandler
{
    private static TaskCompletionSource<int> _handled = New();

    public static Task<int> Handled => _handled.Task;

    public static void Reset() => _handled = New();

    public void Handle(InboxOrder message) => _handled.TrySetResult(message.OrderId);

    private static TaskCompletionSource<int> New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed record ListenedOrder(int OrderId);

public sealed class ListenedOrderHandler
{
    private static TaskCompletionSource<(int, Uri)> _handled = New();

    public static Task<(int OrderId, Uri Destination)> Handled => _handled.Task;

    public static void Reset() => _handled = New();

    public void Handle(ListenedOrder message, Envelope envelope) =>
        _handled.TrySetResult((message.OrderId, envelope.Destination.Value));

    private static TaskCompletionSource<(int, Uri)> New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
