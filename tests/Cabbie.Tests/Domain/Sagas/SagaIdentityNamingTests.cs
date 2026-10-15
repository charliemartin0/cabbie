using Cabbie.Domain.Sagas;

namespace Cabbie.Tests.Domain.Sagas;

public sealed class SagaIdentityNamingTests
{
    [Fact]
    public void saga_identity_attribute_selects_the_instance()
    {
        var sagaId = SagaIdentityNaming.For(new PlaceOrder(42), typeof(OrderSaga));

        Assert.Equal("42", sagaId.Value);
    }

    [Fact]
    public void conventional_saga_type_id_is_used_when_attribute_is_absent()
    {
        var sagaId = SagaIdentityNaming.For(new MessageWithOrderSagaId(7), typeof(OrderSaga));

        Assert.Equal("7", sagaId.Value);
    }

    [Fact]
    public void id_property_is_used_when_attribute_and_convention_are_absent()
    {
        var sagaId = SagaIdentityNaming.For(new MessageWithId(9), typeof(OrderSaga));

        Assert.Equal("9", sagaId.Value);
    }

    [Fact]
    public void attribute_wins_over_convention_and_id()
    {
        var message = new AttributedOverConvention
        {
            OrderId = 1,
            OrderSagaId = 2,
            Id = 3
        };

        var sagaId = SagaIdentityNaming.For(message, typeof(OrderSaga));

        Assert.Equal("1", sagaId.Value);
    }

    [Fact]
    public void charge_payment_style_order_id_does_not_belong_to_order_saga()
    {
        var sagaId = SagaIdentityNaming.For(new ChargePayment(42), typeof(OrderSaga));

        Assert.Equal("", sagaId.Value);
    }

    [Fact]
    public void missing_identity_is_empty_not_an_error()
    {
        var sagaId = SagaIdentityNaming.For(new UnaliasedMessage(), typeof(OrderSaga));

        Assert.Equal("", sagaId.Value);
    }

    [Fact]
    public void null_identity_value_is_empty()
    {
        var sagaId = SagaIdentityNaming.For(new NullableSagaKey(), typeof(OrderSaga));

        Assert.Equal("", sagaId.Value);
    }

    [Fact]
    public void timeout_message_still_correlates_by_saga_identity()
    {
        var sagaId = SagaIdentityNaming.For(new OrderTimeout(42), typeof(OrderSaga));

        Assert.Equal("42", sagaId.Value);
    }

    [Fact]
    public void can_correlate_matches_the_property_walk_without_a_message_instance()
    {
        Assert.True(SagaIdentityNaming.CanCorrelate(typeof(PlaceOrder), typeof(OrderSaga)));
        Assert.True(SagaIdentityNaming.CanCorrelate(typeof(MessageWithOrderSagaId), typeof(OrderSaga)));
        Assert.True(SagaIdentityNaming.CanCorrelate(typeof(MessageWithId), typeof(OrderSaga)));
        Assert.True(SagaIdentityNaming.CanCorrelate(typeof(OrderTimeout), typeof(OrderSaga)));
        Assert.False(SagaIdentityNaming.CanCorrelate(typeof(ChargePayment), typeof(OrderSaga)));
        Assert.False(SagaIdentityNaming.CanCorrelate(typeof(UnaliasedMessage), typeof(OrderSaga)));
    }

    [Fact]
    public void null_message_or_saga_type_throws()
    {
        Assert.Throws<ArgumentNullException>(() => SagaIdentityNaming.For(null!, typeof(OrderSaga)));
        Assert.Throws<ArgumentNullException>(() => SagaIdentityNaming.For(new PlaceOrder(1), null!));
        Assert.Throws<ArgumentNullException>(() => SagaIdentityNaming.CanCorrelate(null!, typeof(OrderSaga)));
        Assert.Throws<ArgumentNullException>(() => SagaIdentityNaming.CanCorrelate(typeof(PlaceOrder), null!));
    }

    private sealed record NullableSagaKey
    {
        [SagaIdentity]
        public int? OrderId { get; init; }
    }
}
