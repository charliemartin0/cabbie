using Cabbie.Application.Discovery;
using Cabbie.Tests.Application.Discovery.InvalidSignatures;
using Cabbie.Tests.Domain;

namespace Cabbie.Tests.Application.Discovery;

public sealed class HandlerConventionTests
{
    [Fact]
    public void public_instance_handle_is_a_handler()
    {
        var discovered = HandlerConvention.For(DiscoveryMethods.PublicOn<PlaceOrderHandler>(nameof(PlaceOrderHandler.Handle)));

        Assert.NotNull(discovered);
        Assert.Equal(typeof(PlaceOrder), discovered.MessageClrType);
        Assert.Equal(typeof(PlaceOrderHandler), discovered.HandlerType);
        Assert.False(discovered.IsStatic);
        Assert.Empty(discovered.InjectionSlots);
        Assert.Equal(nameof(PlaceOrderHandler.Handle), discovered.Method.Name);
    }

    [Fact]
    public void public_static_consume_is_a_handler()
    {
        var discovered = HandlerConvention.For(DiscoveryMethods.PublicOn(typeof(LogIncidentConsumer), nameof(LogIncidentConsumer.Consume)));

        Assert.NotNull(discovered);
        Assert.Equal(typeof(LogIncident), discovered.MessageClrType);
        Assert.Equal(typeof(LogIncidentConsumer), discovered.HandlerType);
        Assert.True(discovered.IsStatic);
        Assert.Empty(discovered.InjectionSlots);
    }

    [Fact]
    public void handle_async_is_a_handler()
    {
        var discovered = HandlerConvention.For(DiscoveryMethods.PublicOn<PingAsyncHandler>(nameof(PingAsyncHandler.HandleAsync)));

        Assert.NotNull(discovered);
        Assert.Equal(typeof(PingAsync), discovered.MessageClrType);
        Assert.False(discovered.IsStatic);
    }

    [Fact]
    public void consume_async_is_a_handler()
    {
        var discovered = HandlerConvention.For(DiscoveryMethods.PublicOn<ThingHappenedConsumer>(nameof(ThingHappenedConsumer.ConsumeAsync)));

        Assert.NotNull(discovered);
        Assert.Equal(typeof(ThingHappened), discovered.MessageClrType);
    }

    [Fact]
    public void start_is_a_handler()
    {
        var discovered = HandlerConvention.For(DiscoveryMethods.PublicOn<DiscoveredOrderSaga>(nameof(DiscoveredOrderSaga.Start)));

        Assert.NotNull(discovered);
        Assert.Equal(typeof(PlaceOrder), discovered.MessageClrType);
        Assert.Equal(typeof(DiscoveredOrderSaga), discovered.HandlerType);
        Assert.False(discovered.IsStatic);
    }

    [Fact]
    public void start_async_is_a_handler()
    {
        var discovered = HandlerConvention.For(DiscoveryMethods.PublicOn<StartAsyncSaga>(nameof(StartAsyncSaga.StartAsync)));

        Assert.NotNull(discovered);
        Assert.Equal(typeof(StartAsyncMessage), discovered.MessageClrType);
    }

    [Fact]
    public void extra_parameter_that_is_a_service_is_an_injection_slot()
    {
        DiscoveredHandler? discovered = HandlerConvention.For(
            DiscoveryMethods.PublicOn<ChargePaymentHandler>(nameof(ChargePaymentHandler.HandleAsync)));

        Assert.NotNull(discovered);
        Assert.Contains(discovered.InjectionSlots, slot => slot.ParameterType == typeof(IPaymentGateway));
    }

    [Fact]
    public void process_is_not_a_handler()
    {
        var discovered = HandlerConvention.For(DiscoveryMethods.PublicOn<NotAHandler>(nameof(NotAHandler.Process)));

        Assert.Null(discovered);
    }

    [Fact]
    public void private_handle_is_not_a_handler()
    {
        var discovered = HandlerConvention.For(DiscoveryMethods.NonPublicOn<HiddenHandler>("Handle"));

        Assert.Null(discovered);
    }

    [Fact]
    public void handle_without_a_message_parameter_is_not_a_handler()
    {
        var discovered = HandlerConvention.For(DiscoveryMethods.PublicOn<ParameterlessHandler>(nameof(ParameterlessHandler.Handle)));

        Assert.Null(discovered);
    }

    [Fact]
    public void open_generic_handle_is_not_a_handler()
    {
        var discovered = HandlerConvention.For(DiscoveryMethods.PublicOn<GenericHandler>(nameof(GenericHandler.Handle)));

        Assert.Null(discovered);
    }

    [Fact]
    public void null_method_throws()
    {
        Assert.Throws<ArgumentNullException>(() => HandlerConvention.For(null!));
    }
}
