using Cabbie.Application.Bus;
using Cabbie.Application.Cascades;
using Cabbie.Application.Discovery;
using Cabbie.Tests.Handlers;

namespace Cabbie.Tests.Application.Cascades;

/// <summary>
/// Prove-with for the ValueTask&lt;T&gt; slice: ValueTask returns are unpacked to their
/// TMessage body through the cached AsTask unwrap — the wrapper never leaks into the
/// cascade list, and the Executor awaits async ValueTask handlers before the unpack.
/// </summary>
public sealed class ValueTaskResultAccessorTests
{
    [Fact]
    public void value_task_of_tmessage_unwraps_to_tmessage_body()
    {
        ValueTask<ChargePayment> valueTask = new(new ChargePayment(7));

        IReadOnlyList<object> outgoing = CascadingMessages.From(valueTask);

        var payment = Assert.IsType<ChargePayment>(Assert.Single(outgoing));
        Assert.Equal(7, payment.OrderId);
    }

    [Fact]
    public void value_task_result_accessor_caches_unwrap_function_per_type()
    {
        Task first = TaskResultAccessor.AsTask(new ValueTask<ChargePayment>(new ChargePayment(1)));
        Assert.NotNull(first);

        Func<object, Task> cached = TaskResultAccessor.ValueTaskToTaskCache[typeof(ValueTask<ChargePayment>)];
        Task second = TaskResultAccessor.AsTask(new ValueTask<ChargePayment>(new ChargePayment(2)));
        Assert.NotNull(second);

        // The second call reused the cached delegate — the entry was not rebuilt.
        Assert.Same(cached, TaskResultAccessor.ValueTaskToTaskCache[typeof(ValueTask<ChargePayment>)]);
    }

    [Fact]
    public async Task async_value_task_handler_cascade_unwraps_after_the_executor_awaits()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ValueTaskHandler));
        var cascades = new RecordingCascadePublisher();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, cascades);

        await bus.InvokeAsync(new ValueTaskCommand(7));

        var payment = Assert.IsType<ChargePayment>(Assert.Single(cascades.Published));
        Assert.Equal(7, payment.OrderId);
    }

    private sealed class RecordingCascadePublisher : ICascadePublisher
    {
        public List<object> Published { get; } = [];

        public void Publish(IReadOnlyList<object> outgoing) => Published.AddRange(outgoing);
    }
}

public sealed record ValueTaskCommand(int OrderId);

/// <summary>
/// Genuinely async ValueTask return — the Executor must await it before the unpack.
/// </summary>
public sealed class ValueTaskHandler
{
    public async ValueTask<ChargePayment> Handle(ValueTaskCommand message)
    {
        await Task.Yield();
        return new ChargePayment(message.OrderId);
    }
}