using Cabbie.Application.Cascades;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Cascades;

public sealed class CascadingMessagesTests
{
    [Fact]
    public void single_object_is_one_outgoing_message()
    {
        var payment = new ChargePayment(1);

        IReadOnlyList<object> outgoing = CascadingMessages.From(payment);

        Assert.Same(payment, Assert.Single(outgoing));
    }

    [Fact]
    public void throwing_handler_publishes_nothing()
    {
        object? result = null;
        try
        {
            result = new ThrowsAfterBuildingPayment().Handle(new PlaceOrder(1));
            Assert.Fail("expected the handler to throw");
        }
        catch (TimeoutException)
        {
        }

        Assert.Empty(CascadingMessages.From(result));
    }

    [Fact]
    public void succeeding_handler_publishes_exactly_its_return_values_after_it_returns()
    {
        var handler = new ChargePaymentOnSuccess();
        Assert.False(handler.Returned);

        object? result = handler.Handle(new PlaceOrder(1));

        Assert.True(handler.Returned);
        Assert.Equal([handler.Payment], CascadingMessages.From(result));
    }

    [Fact]
    public void tuple_elements_are_outgoing_in_order()
    {
        var timeout = new OrderTimeout(1);
        var payment = new ChargePayment(1);

        IReadOnlyList<object> outgoing = CascadingMessages.From((timeout, payment));

        Assert.Equal([timeout, payment], outgoing);
    }

    [Fact]
    public void saga_in_a_tuple_is_not_an_outgoing_message()
    {
        var saga = new OrderSaga { Id = 1 };
        var timeout = new OrderTimeout(1);
        var payment = new ChargePayment(1);

        IReadOnlyList<object> outgoing = CascadingMessages.From((saga, timeout, payment));

        Assert.Equal([timeout, payment], outgoing);
    }

    [Fact]
    public void null_result_is_no_outgoing_messages()
    {
        Assert.Empty(CascadingMessages.From(null));
    }

    [Fact]
    public void completed_task_is_no_outgoing_messages()
    {
        Assert.Empty(CascadingMessages.From(Task.CompletedTask));
    }

    [Fact]
    public void task_result_is_unwrapped()
    {
        var payment = new ChargePayment(1);

        IReadOnlyList<object> outgoing = CascadingMessages.From(Task.FromResult(payment));

        Assert.Same(payment, Assert.Single(outgoing));
    }

    [Fact]
    public void string_is_one_message_not_characters()
    {
        IReadOnlyList<object> outgoing = CascadingMessages.From("nope");

        Assert.Equal(["nope"], outgoing);
    }

    [Fact]
    public void enumerable_items_are_outgoing_in_order()
    {
        var timeout = new OrderTimeout(1);
        var payment = new ChargePayment(1);

        IReadOnlyList<object> outgoing = CascadingMessages.From(new object[] { timeout, payment });

        Assert.Equal([timeout, payment], outgoing);
    }

    [Fact]
    public void outgoing_messages_bag_is_flattened()
    {
        var timeout = new OrderTimeout(1);
        var payment = new ChargePayment(1);
        var outgoingMessages = new OutgoingMessages { timeout, payment };

        IReadOnlyList<object> outgoing = CascadingMessages.From(outgoingMessages);

        Assert.Equal([timeout, payment], outgoing);
    }

    [Fact]
    public void null_tuple_elements_are_skipped()
    {
        var timeout = new OrderTimeout(1);
        var payment = new ChargePayment(1);

        IReadOnlyList<object> outgoing = CascadingMessages.From((timeout, (ChargePayment?)null, payment));

        Assert.Equal([timeout, payment], outgoing);
    }

    [Fact]
    public void envelope_is_not_an_outgoing_message()
    {
        Assert.Empty(CascadingMessages.From(EnvelopeFactory.Create()));
    }

    [Fact]
    public void scheduled_cascade_is_one_outgoing_item_not_unwrapped()
    {
        var wrapper = new ScheduledCascade(new ChargePayment(1), DateTimeOffset.UtcNow.AddSeconds(2));

        IReadOnlyList<object> outgoing = CascadingMessages.From(new object[] { wrapper });

        Assert.Same(wrapper, Assert.Single(outgoing));
    }

    [Fact]
    public void saga_alone_is_not_an_outgoing_message()
    {
        Assert.Empty(CascadingMessages.From(new OrderSaga { Id = 1 }));
    }
}

public sealed class ChargePaymentOnSuccess
{
    public ChargePayment Payment { get; } = new(1);

    public bool Returned { get; private set; }

    public ChargePayment Handle(PlaceOrder message)
    {
        Returned = true;
        return Payment;
    }
}

public sealed class ThrowsAfterBuildingPayment
{
    public ChargePayment Handle(PlaceOrder message)
    {
        _ = new ChargePayment(message.OrderId);
        throw new TimeoutException("Payment gateway timeout");
    }
}
