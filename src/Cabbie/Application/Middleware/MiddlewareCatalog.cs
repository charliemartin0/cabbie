using Cabbie.Application.Discovery;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Middleware;

/// <summary>
/// Ordered outer and inner wrapper lists. A table of rules, not a second control-flow language.
/// First registered on a layer is outermost. Matching is additive and exact CLR type.
/// </summary>
public sealed class MiddlewareCatalog
{
    private readonly List<Registration> _outer = [];
    private readonly List<Registration> _inner = [];

    public void Register(MiddlewareLayer layer, IMessageMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        ListFor(layer).Add(new Registration(middleware, MessageType: null, HandlerType: null));
    }

    public void RegisterForMessage<TMessage>(MiddlewareLayer layer, IMessageMiddleware middleware) =>
        RegisterForMessage(layer, typeof(TMessage), middleware);

    public void RegisterForMessage(MiddlewareLayer layer, Type messageType, IMessageMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(middleware);
        ListFor(layer).Add(new Registration(middleware, messageType, HandlerType: null));
    }

    public void RegisterForHandler<THandler>(MiddlewareLayer layer, IMessageMiddleware middleware) =>
        RegisterForHandler(layer, typeof(THandler), middleware);

    public void RegisterForHandler(MiddlewareLayer layer, Type handlerType, IMessageMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentNullException.ThrowIfNull(middleware);
        ListFor(layer).Add(new Registration(middleware, MessageType: null, handlerType));
    }

    public IReadOnlyList<IMessageMiddleware> For(MiddlewareLayer layer, DiscoveredHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return [.. ListFor(layer).Where(registration => registration.Matches(handler))
            .Select(registration => registration.Middleware)];
    }

    public Task<object?> InvokeAsync(
        MiddlewareLayer layer,
        Envelope envelope,
        DiscoveredHandler handler,
        Func<Task<object?>> innermost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(innermost);

        IReadOnlyList<IMessageMiddleware> wrappers = For(layer, handler);
        Func<Task<object?>> next = innermost;
        for (int i = wrappers.Count - 1; i >= 0; i--)
        {
            IMessageMiddleware wrapper = wrappers[i];
            Func<Task<object?>> inner = next;
            next = () => InvokeOne(wrapper, envelope, handler, inner, cancellationToken);
        }

        return next();
    }

    private List<Registration> ListFor(MiddlewareLayer layer) =>
        layer switch
        {
            MiddlewareLayer.Outer => _outer,
            MiddlewareLayer.Inner => _inner,
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, message: null)
        };

    private static async Task<object?> InvokeOne(
        IMessageMiddleware wrapper,
        Envelope envelope,
        DiscoveredHandler handler,
        Func<Task<object?>> inner,
        CancellationToken cancellationToken)
    {
        int calls = 0;
        async Task<object?> counted()
        {
            calls++;
            if (calls > 1)
            {
                throw new MiddlewareNextViolation();
            }

            return await inner();
        }

        object? result = await wrapper.InvokeAsync(envelope, handler, counted, cancellationToken);
        if (calls != 1)
        {
            throw new MiddlewareNextViolation();
        }

        return result;
    }

    private sealed record Registration(IMessageMiddleware Middleware, Type? MessageType, Type? HandlerType)
    {
        public bool Matches(DiscoveredHandler handler) =>
            (MessageType is null && HandlerType is null)
            || (MessageType is not null && MessageType == handler.MessageClrType)
            || (HandlerType is not null && HandlerType == handler.HandlerType);
    }
}
