using Cabbie.Application.Bus;
using Cabbie.Application.Cascades;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;
using Cabbie.Tests.Domain;

namespace Cabbie.Tests.Application.Mediator;

public sealed class MediatorTests
{
    [Fact]
    public async Task invoke_async_does_not_return_until_handle_returns()
    {
        RecordedIncidentHandler.Handled = false;

        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(RecordedIncidentHandler));

        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog);

        await bus.InvokeAsync(new RecordedIncident("Everything broken"));

        Assert.True(RecordedIncidentHandler.Handled);
    }

    [Fact]
    public async Task invoke_publishes_return_values_after_the_handler_succeeds()
    {
        ReturnsChargePaymentHandler.Returned = false;
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ReturnsChargePaymentHandler));
        var cascades = new RecordingCascadePublisher();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, cascades);

        await bus.InvokeAsync(new PlaceOrder(1));

        Assert.True(ReturnsChargePaymentHandler.Returned);
        var payment = Assert.IsType<ChargePayment>(Assert.Single(cascades.Published));
        Assert.Equal(1, payment.OrderId);
    }

    [Fact]
    public async Task invoke_of_throwing_handler_publishes_nothing()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ThrowingPlaceOrderHandler));
        var cascades = new RecordingCascadePublisher();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, cascades);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => bus.InvokeAsync(new PlaceOrder(1)));

        Assert.IsType<TimeoutException>(fault.InnerException);
        Assert.Empty(cascades.Published);
    }

    [Fact]
    public async Task invoke_retries_the_same_envelope_then_succeeds()
    {
        FlakyPlaceOrderHandler.SeenAttempts.Clear();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(FlakyPlaceOrderHandler));
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Retry().Retry().Then.MoveToErrorQueue();
        var cascades = new RecordingCascadePublisher();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(
            catalog,
            cascades,
            new Executor(policies));

        await bus.InvokeAsync(new PlaceOrder(1));

        Assert.Equal([1, 2, 3], FlakyPlaceOrderHandler.SeenAttempts);
        var payment = Assert.IsType<ChargePayment>(Assert.Single(cascades.Published));
        Assert.Equal(1, payment.OrderId);
    }

    [Fact]
    public async Task invoke_without_a_handler_throws_handler_not_found()
    {
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(new HandlerCatalog());

        HandlerNotFound missing = await Assert.ThrowsAsync<HandlerNotFound>(
            () => bus.InvokeAsync(new PlaceOrder(1)));

        Assert.Equal(typeof(PlaceOrder), missing.MessageType);
    }

    [Fact]
    public async Task invoke_does_not_run_handlers_for_cascaded_messages()
    {
        ChargePaymentWasHandled.Handled = false;
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ReturnsChargePaymentHandler));
        catalog.Scan(typeof(ChargePaymentWasHandled));
        var cascades = new RecordingCascadePublisher();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, cascades);

        await bus.InvokeAsync(new PlaceOrder(1));

        Assert.False(ChargePaymentWasHandled.Handled);
        Assert.Single(cascades.Published);
    }
}

public sealed record RecordedIncident(string Description);

public sealed class RecordedIncidentHandler
{
    public static bool Handled;

    public void Handle(RecordedIncident message)
    {
        Handled = true;
    }
}

public sealed class ReturnsChargePaymentHandler
{
    public static bool Returned;

    public ChargePayment Handle(PlaceOrder message)
    {
        Returned = true;
        return new ChargePayment(message.OrderId);
    }
}

public sealed class ThrowingPlaceOrderHandler
{
    public ChargePayment Handle(PlaceOrder message)
    {
        _ = new ChargePayment(message.OrderId);
        throw new TimeoutException("Payment gateway timeout");
    }
}

public sealed class FlakyPlaceOrderHandler
{
    public static List<int> SeenAttempts { get; } = [];

    public ChargePayment Handle(PlaceOrder message, Envelope envelope)
    {
        SeenAttempts.Add(envelope.Attempts.Value);
        if (envelope.Attempts.Value < 3)
        {
            throw new TimeoutException("Payment gateway timeout");
        }

        return new ChargePayment(message.OrderId);
    }
}

public sealed class ChargePaymentWasHandled
{
    public static bool Handled;

    public void Handle(ChargePayment message)
    {
        Handled = true;
    }
}

public sealed class RecordingCascadePublisher : ICascadePublisher
{
    public List<object> Published { get; } = [];

    public void Publish(IReadOnlyList<object> outgoing)
    {
        Published.AddRange(outgoing);
    }
}
