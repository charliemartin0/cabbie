using Cabbie.Domain.Messaging;

namespace Cabbie.Tests.Domain.Messaging;

public sealed class MessageTypeNamingTests
{
    [Fact]
    public void attribute_alias_wins_over_full_name()
    {
        var name = MessageTypeNaming.For(typeof(AliasedPlaceOrder));

        Assert.Equal("place-order", name.Value);
        Assert.NotEqual(typeof(AliasedPlaceOrder).FullName, name.Value);
    }

    [Fact]
    public void absent_attribute_uses_full_name_not_name_or_assembly_qualified_name()
    {
        var type = typeof(UnaliasedMessage);
        var name = MessageTypeNaming.For(type);

        Assert.Equal(type.FullName, name.Value);
        Assert.NotEqual(type.Name, name.Value);
        Assert.NotEqual(type.AssemblyQualifiedName, name.Value);
    }

    [Fact]
    public void two_types_with_the_same_short_name_get_distinct_wire_names()
    {
        var orders = MessageTypeNaming.For(typeof(Orders.PlaceOrder));
        var billing = MessageTypeNaming.For(typeof(Billing.PlaceOrder));

        Assert.Equal(typeof(Orders.PlaceOrder).FullName, orders.Value);
        Assert.Equal(typeof(Billing.PlaceOrder).FullName, billing.Value);
        Assert.NotEqual(orders, billing);
        Assert.NotEqual("PlaceOrder", orders.Value);
    }

    [Fact]
    public void null_type_throws()
    {
        Assert.Throws<ArgumentNullException>(() => MessageTypeNaming.For(null!));
    }
}

file static class Orders
{
    public sealed record PlaceOrder;
}

file static class Billing
{
    public sealed record PlaceOrder;
}
