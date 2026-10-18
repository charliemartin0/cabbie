using Cabbie.Application.Routing;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Tests.Domain;

namespace Cabbie.Tests.Application.Routing;

public sealed class RoutingCatalogTests
{
    [Fact]
    public void charge_payment_gets_local_payments_without_the_saga_naming_a_queue()
    {
        var routing = new RoutingCatalog();
        routing.PublishMessage<ChargePayment>().ToLocalQueue("payments");

        ChargePayment cascaded = new(1);
        PlaceOrder start = new(1);

        Assert.Equal(new Destination(new Uri("local://payments/")), routing.For(cascaded));
        Assert.Equal(new Destination(new Uri("local://placeorder/")), routing.For(start));
    }

    [Fact]
    public void unconfigured_type_gets_a_default_local_queue_named_after_the_type()
    {
        var routing = new RoutingCatalog();

        Destination destination = routing.For(typeof(PlaceOrder));

        Assert.Equal(new Destination(new Uri("local://placeorder/")), destination);
    }

    [Fact]
    public void local_queue_attribute_sets_the_destination()
    {
        var routing = new RoutingCatalog();

        Destination destination = routing.For(typeof(AttributedChargePayment));

        Assert.Equal(new Destination(new Uri("local://payments/")), destination);
    }

    [Fact]
    public void publish_message_rule_overrides_local_queue_attribute()
    {
        var routing = new RoutingCatalog();
        routing.PublishMessage<InboxPayment>().ToLocalQueue("payments");

        Destination destination = routing.For(typeof(InboxPayment));

        Assert.Equal(new Destination(new Uri("local://payments/")), destination);
    }

    [Fact]
    public void to_records_rabbitmq_and_tcp_uris()
    {
        var routing = new RoutingCatalog();
        routing.PublishMessage<ChargePayment>().To(new Uri("rabbitmq://payments/"));
        routing.PublishMessage<PlaceOrder>().To(new Uri("tcp://127.0.0.1:5555/"));

        Assert.Equal(new Destination(new Uri("rabbitmq://payments/")), routing.For(typeof(ChargePayment)));
        Assert.Equal(new Destination(new Uri("tcp://127.0.0.1:5555/")), routing.For(typeof(PlaceOrder)));
    }

    [Fact]
    public void for_object_uses_the_runtime_type()
    {
        var routing = new RoutingCatalog();
        routing.PublishMessage<ChargePayment>().ToLocalQueue("payments");

        object cascaded = new ChargePayment(1);

        Assert.Equal(new Destination(new Uri("local://payments/")), routing.For(cascaded));
    }

    [Fact]
    public void null_arguments_throw()
    {
        var routing = new RoutingCatalog();

        Assert.Throws<ArgumentNullException>(() => routing.PublishMessage(null!));
        Assert.Throws<ArgumentNullException>(() => routing.For((Type)null!));
        Assert.Throws<ArgumentNullException>(() => routing.For((object)null!));
        Assert.Throws<ArgumentNullException>(() => routing.PublishMessage<ChargePayment>().ToLocalQueue(null!));
        Assert.Throws<ArgumentNullException>(() => routing.PublishMessage<ChargePayment>().To((Uri)null!));
    }
}

[LocalQueue("payments")]
public sealed record AttributedChargePayment(int OrderId);

[LocalQueue("inbox")]
public sealed record InboxPayment(int OrderId);
