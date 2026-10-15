using Cabbie.Application.Cascades;
using Cabbie.Tests.Handlers;

namespace Cabbie.Tests.Application.Cascades;

/// <summary>
/// Prove-with for Perf #3: Task&lt;T&gt;.Result is unwrapped through a cached compiled
/// accessor, not a per-call GetProperty reflection.
/// </summary>
public sealed class TaskResultAccessorTests
{
    [Fact]
    public void task_result_accessor_caches_unwrap_function_per_type()
    {
        object? first = TaskResultAccessor.GetResult(Task.FromResult(new ChargePayment(1)));
        Assert.Equal(new ChargePayment(1), first);

        Func<Task, object?> cached = TaskResultAccessor.Cache[typeof(Task<ChargePayment>)];
        object? second = TaskResultAccessor.GetResult(Task.FromResult(new ChargePayment(2)));
        Assert.Equal(new ChargePayment(2), second);

        // The second call reused the cached delegate — the entry was not rebuilt.
        Assert.Same(cached, TaskResultAccessor.Cache[typeof(Task<ChargePayment>)]);
    }

    [Fact]
    public void cascading_messages_from_sees_the_tmessage_body_not_the_completed_task()
    {
        Task<ChargePayment> task = Task.FromResult(new ChargePayment(7));

        IReadOnlyList<object> outgoing = CascadingMessages.From(task);

        var payment = Assert.IsType<ChargePayment>(Assert.Single(outgoing));
        Assert.Equal(7, payment.OrderId);
    }
}