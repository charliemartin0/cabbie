using Cabbie.Application.Discovery;
using Cabbie.Application.Middleware;
using Cabbie.Domain.Envelope;
using Cabbie.Tests.Domain;

namespace Cabbie.Tests.Application.Middleware;

public sealed class MiddlewareCatalogTests
{
    [Fact]
    public void global_matches_every_handler()
    {
        var catalog = new MiddlewareCatalog();
        var wrapper = new RecordingMiddleware();
        catalog.Register(MiddlewareLayer.Inner, wrapper);

        Assert.Same(wrapper, Assert.Single(catalog.For(MiddlewareLayer.Inner, HandlerFor<ChargePaymentHandler>())));
        Assert.Same(wrapper, Assert.Single(catalog.For(MiddlewareLayer.Inner, HandlerFor<PlaceOrderHandler>())));
        Assert.Empty(catalog.For(MiddlewareLayer.Outer, HandlerFor<ChargePaymentHandler>()));
    }

    [Fact]
    public void message_type_matches_only_that_clr_type()
    {
        var catalog = new MiddlewareCatalog();
        var wrapper = new RecordingMiddleware();
        catalog.RegisterForMessage<ChargePayment>(MiddlewareLayer.Inner, wrapper);

        Assert.Same(wrapper, Assert.Single(catalog.For(MiddlewareLayer.Inner, HandlerFor<ChargePaymentHandler>())));
        Assert.Empty(catalog.For(MiddlewareLayer.Inner, HandlerFor<PlaceOrderHandler>()));
    }

    [Fact]
    public void handler_type_matches_only_that_clr_type()
    {
        var catalog = new MiddlewareCatalog();
        var wrapper = new RecordingMiddleware();
        catalog.RegisterForHandler<ChargePaymentHandler>(MiddlewareLayer.Inner, wrapper);

        Assert.Same(wrapper, Assert.Single(catalog.For(MiddlewareLayer.Inner, HandlerFor<ChargePaymentHandler>())));
        Assert.Empty(catalog.For(MiddlewareLayer.Inner, HandlerFor<OtherChargePaymentHandler>()));
    }

    [Fact]
    public void matching_registrations_are_additive_in_catalog_order()
    {
        var catalog = new MiddlewareCatalog();
        var global = new RecordingMiddleware();
        var byMessage = new RecordingMiddleware();
        var byHandler = new RecordingMiddleware();
        catalog.Register(MiddlewareLayer.Inner, global);
        catalog.RegisterForMessage<ChargePayment>(MiddlewareLayer.Inner, byMessage);
        catalog.RegisterForHandler<ChargePaymentHandler>(MiddlewareLayer.Inner, byHandler);

        Assert.Equal(
            [global, byMessage, byHandler],
            catalog.For(MiddlewareLayer.Inner, HandlerFor<ChargePaymentHandler>()));
    }

    [Fact]
    public void same_instance_can_be_registered_on_both_layers()
    {
        var catalog = new MiddlewareCatalog();
        var wrapper = new RecordingMiddleware();
        catalog.Register(MiddlewareLayer.Outer, wrapper);
        catalog.Register(MiddlewareLayer.Inner, wrapper);

        DiscoveredHandler handler = HandlerFor<ChargePaymentHandler>();
        Assert.Same(wrapper, Assert.Single(catalog.For(MiddlewareLayer.Outer, handler)));
        Assert.Same(wrapper, Assert.Single(catalog.For(MiddlewareLayer.Inner, handler)));
    }

    [Fact]
    public async Task skipped_next_throws_middleware_next_violation()
    {
        var catalog = new MiddlewareCatalog();
        catalog.Register(MiddlewareLayer.Inner, new SkipNextMiddleware());

        await Assert.ThrowsAsync<MiddlewareNextViolation>(
            () => catalog.InvokeAsync(
                MiddlewareLayer.Inner,
                EnvelopeFactoryEnvelope(),
                HandlerFor<ChargePaymentHandler>(),
                () => Task.FromResult<object?>(new ChargePayment(1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task double_next_throws_middleware_next_violation()
    {
        var catalog = new MiddlewareCatalog();
        catalog.Register(MiddlewareLayer.Inner, new DoubleNextMiddleware());

        await Assert.ThrowsAsync<MiddlewareNextViolation>(
            () => catalog.InvokeAsync(
                MiddlewareLayer.Inner,
                EnvelopeFactoryEnvelope(),
                HandlerFor<ChargePaymentHandler>(),
                () => Task.FromResult<object?>(new ChargePayment(1)),
                CancellationToken.None));
    }

    [Fact]
    public async Task skipped_next_does_not_use_the_returned_value()
    {
        var catalog = new MiddlewareCatalog();
        catalog.Register(MiddlewareLayer.Inner, new SkipNextMiddleware());
        bool innermostRan = false;

        await Assert.ThrowsAsync<MiddlewareNextViolation>(
            () => catalog.InvokeAsync(
                MiddlewareLayer.Inner,
                EnvelopeFactoryEnvelope(),
                HandlerFor<ChargePaymentHandler>(),
                () =>
                {
                    innermostRan = true;
                    return Task.FromResult<object?>(new ChargePayment(1));
                },
                CancellationToken.None));

        Assert.False(innermostRan);
    }

    [Fact]
    public async Task first_registered_is_outermost()
    {
        var catalog = new MiddlewareCatalog();
        var order = new List<string>();
        catalog.Register(MiddlewareLayer.Inner, new OrderedMiddleware("a", order));
        catalog.Register(MiddlewareLayer.Inner, new OrderedMiddleware("b", order));

        await catalog.InvokeAsync(
            MiddlewareLayer.Inner,
            EnvelopeFactoryEnvelope(),
            HandlerFor<ChargePaymentHandler>(),
            () =>
            {
                order.Add("handle");
                return Task.FromResult<object?>(null);
            },
            CancellationToken.None);

        Assert.Equal(["a-before", "b-before", "handle", "b-after", "a-after"], order);
    }

    private static DiscoveredHandler HandlerFor<THandler>()
    {
        var handlers = new HandlerCatalog();
        handlers.Scan(typeof(THandler));
        return Assert.Single(handlers.Handlers);
    }

    private static Envelope EnvelopeFactoryEnvelope() =>
        Cabbie.Tests.Domain.Envelope.EnvelopeFactory.Create(
            message: new Cabbie.Domain.Messaging.ValueObjects.Message(new ChargePayment(1)),
            messageType: Cabbie.Domain.Messaging.MessageTypeNaming.For(typeof(ChargePayment)));
}

public sealed class ChargePaymentHandler
{
    public ChargePayment Handle(ChargePayment payment) => payment;
}

public sealed class OtherChargePaymentHandler
{
    public ChargePayment Handle(ChargePayment payment) => payment;
}

public sealed class PlaceOrderHandler
{
    public void Handle(PlaceOrder message)
    {
    }
}

public sealed class RecordingMiddleware : IMessageMiddleware
{
    public int Calls { get; private set; }
    public List<Type> HandlerTypes { get; } = [];

    public async Task<object?> InvokeAsync(
        Envelope envelope,
        DiscoveredHandler handler,
        Func<Task<object?>> next,
        CancellationToken cancellationToken)
    {
        Calls++;
        HandlerTypes.Add(handler.HandlerType);
        return await next();
    }
}

public sealed class SkipNextMiddleware : IMessageMiddleware
{
    public Task<object?> InvokeAsync(
        Envelope envelope,
        DiscoveredHandler handler,
        Func<Task<object?>> next,
        CancellationToken cancellationToken) =>
        Task.FromResult<object?>(new ChargePayment(99));
}

public sealed class DoubleNextMiddleware : IMessageMiddleware
{
    public async Task<object?> InvokeAsync(
        Envelope envelope,
        DiscoveredHandler handler,
        Func<Task<object?>> next,
        CancellationToken cancellationToken)
    {
        _ = await next();
        return await next();
    }
}

public sealed class OrderedMiddleware : IMessageMiddleware
{
    private readonly string _name;
    private readonly List<string> _order;

    public OrderedMiddleware(string name, List<string> order)
    {
        _name = name;
        _order = order;
    }

    public async Task<object?> InvokeAsync(
        Envelope envelope,
        DiscoveredHandler handler,
        Func<Task<object?>> next,
        CancellationToken cancellationToken)
    {
        _order.Add($"{_name}-before");
        object? result = await next();
        _order.Add($"{_name}-after");
        return result;
    }
}
