using Cabbie.Application.Bus;
using Cabbie.Application.Cascades;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Scheduling;
using Cabbie.Domain.Envelope;
using Cabbie.Infrastructure.Scheduling;
using Cabbie.Tests.Domain;

namespace Cabbie.Tests.Application.Scheduling;

public sealed class MessageSchedulerTests
{
    public MessageSchedulerTests()
    {
        PlayedTimeoutHandler.Handled.Clear();
        ScheduledStartHandler.Started = false;
        FlakyScheduledTimeoutHandler.SeenAttempts.Clear();
        FailingThenPendingTimeoutHandler.Seen.Clear();
    }

    [Fact]
    public async Task a_one_minute_timeout_can_be_played_now()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ScheduledStartHandler));
        catalog.Scan(typeof(PlayedTimeoutHandler));
        var executor = new Executor(new ErrorPolicyCatalog(), scheduled: hold);
        var bus = new Cabbie.Application.Mediator.Mediator(catalog, executor: executor, hold: hold);
        var scheduler = new MessageScheduler(new MessageDelivery(catalog, executor, hold), hold);

        await bus.InvokeAsync(new PlaceOrder(1));

        Assert.True(ScheduledStartHandler.Started);
        Assert.Empty(PlayedTimeoutHandler.Handled);
        var parked = Assert.Single(hold.Peek());
        Assert.IsType<OrderTimeout>(parked.Message.Value);

        await scheduler.PlayDue(parked.DeliverBy.Value!.Value);

        Assert.Equal([1], PlayedTimeoutHandler.Handled);
        Assert.Empty(hold.Peek());
    }

    [Fact]
    public async Task play_due_does_not_run_work_parked_during_the_drain()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().ScheduleRetry(TimeSpan.FromTicks(1));
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(FlakyScheduledTimeoutHandler));
        var executor = new Executor(policies, scheduled: hold);
        hold.Park(TimeoutEnvelope());
        var scheduler = new MessageScheduler(new MessageDelivery(catalog, executor, hold), hold);

        await scheduler.PlayDue(DateTimeOffset.UtcNow.AddYears(10));

        Assert.Equal([1], FlakyScheduledTimeoutHandler.SeenAttempts);
        var parked = Assert.Single(hold.Peek());
        Assert.Equal(2, parked.Attempts.Value);
    }

    [Fact]
    public async Task play_due_with_nothing_due_is_a_noop()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var catalog = new HandlerCatalog();
        var executor = new Executor(new ErrorPolicyCatalog(), scheduled: hold);
        var scheduler = new MessageScheduler(new MessageDelivery(catalog, executor, hold), hold);

        hold.Park(TimeoutEnvelope(DateTimeOffset.UtcNow.AddHours(1)));

        await scheduler.PlayDue(DateTimeOffset.UtcNow);

        Assert.Single(hold.Peek());
    }

    [Fact]
    public async Task play_due_fail_fast_leaves_unstarted_snapshot_members_held()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(FailingThenPendingTimeoutHandler));
        var policies = new ErrorPolicyCatalog();
        policies.OnUnmatchedException();
        var executor = new Executor(policies, scheduled: hold);
        var scheduler = new MessageScheduler(new MessageDelivery(catalog, executor, hold), hold);
        DateTimeOffset due = DateTimeOffset.UtcNow.AddMinutes(-1);
        hold.Park(TimeoutEnvelope(due, orderId: 1));
        hold.Park(TimeoutEnvelope(due, orderId: 2));

        await Assert.ThrowsAsync<HandlerFault>(() => scheduler.PlayDue(DateTimeOffset.UtcNow));

        Assert.Equal([1], FailingThenPendingTimeoutHandler.Seen);
        var remaining = Assert.Single(hold.Peek());
        Assert.Equal(2, ((OrderTimeout)remaining.Message.Value).OrderId);
    }

    [Fact]
    public async Task invoke_of_a_timeout_type_runs_now_and_does_not_park()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(PlayedTimeoutHandler));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, hold: hold);

        await bus.InvokeAsync(new OrderTimeout(1));

        Assert.Equal([1], PlayedTimeoutHandler.Handled);
        Assert.Empty(hold.Peek());
    }

    [Fact]
    public async Task cancelled_play_due_before_work_starts_leaves_held_envelopes()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(PlayedTimeoutHandler));
        var executor = new Executor(new ErrorPolicyCatalog(), scheduled: hold);
        var scheduler = new MessageScheduler(new MessageDelivery(catalog, executor, hold), hold);
        hold.Park(TimeoutEnvelope());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scheduler.PlayDue(DateTimeOffset.UtcNow, cancelled.Token));

        Assert.Single(hold.Peek());
        Assert.Empty(PlayedTimeoutHandler.Handled);
    }

    [Fact]
    public async Task invoke_with_delay_is_delayed_invoke_not_supported()
    {
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(new HandlerCatalog());

        await Assert.ThrowsAsync<DelayedInvokeNotSupported>(
            () => bus.InvokeAsync(new PlaceOrder(1), new DeliveryOptions { Delay = TimeSpan.FromMinutes(1) }));
    }

    [Fact]
    public async Task invoke_with_delay_and_until_is_ambiguous()
    {
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(new HandlerCatalog());

        await Assert.ThrowsAsync<AmbiguousDeliveryOptions>(
            () => bus.InvokeAsync(
                new PlaceOrder(1),
                new DeliveryOptions { Delay = TimeSpan.Zero, Until = DateTimeOffset.UtcNow }));
    }

    [Fact]
    public async Task publish_timeout_parks_without_a_worker()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(new HandlerCatalog(), hold: hold);

        await bus.PublishAsync(new OrderTimeout(1));

        var parked = Assert.Single(hold.Peek());
        Assert.IsType<OrderTimeout>(parked.Message.Value);
    }

    [Fact]
    public async Task publish_zero_delay_timeout_enqueues_immediately()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var enqueuer = new RecordingPublishEnqueuer();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(
            new HandlerCatalog(),
            hold: hold,
            enqueuer: enqueuer);

        await bus.PublishAsync(new OrderTimeout(1), new DeliveryOptions { Delay = TimeSpan.Zero });

        Assert.Empty(hold.Peek());
        Envelope envelope = Assert.Single(enqueuer.Enqueued);
        Assert.IsType<OrderTimeout>(envelope.Message.Value);
    }

    [Fact]
    public async Task throwing_handler_parks_no_timeout()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ThrowingScheduledStartHandler));
        var cascades = new RecordingImmediate();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, cascades, hold: hold);

        await Assert.ThrowsAsync<HandlerFault>(() => bus.InvokeAsync(new PlaceOrder(1)));

        Assert.Empty(hold.Peek());
        Assert.Empty(cascades.Published);
    }

    private static Envelope TimeoutEnvelope(DateTimeOffset? deliverBy = null, int orderId = 1) =>
        Cabbie.Tests.Domain.Envelope.EnvelopeFactory.Create(
            message: new Cabbie.Domain.Messaging.ValueObjects.Message(new OrderTimeout(orderId)),
            messageType: Cabbie.Domain.Messaging.MessageTypeNaming.For(typeof(OrderTimeout)),
            deliverBy: deliverBy ?? DateTimeOffset.UtcNow.AddMinutes(-1));

    private sealed class RecordingImmediate : ICascadePublisher
    {
        public List<object> Published { get; } = [];

        public void Publish(IReadOnlyList<object> outgoing) => Published.AddRange(outgoing);
    }

    private sealed class RecordingPublishEnqueuer : IPublishEnqueuer
    {
        public List<Envelope> Enqueued { get; } = [];

        public void Enqueue(Envelope envelope) => Enqueued.Add(envelope);
    }
}

public sealed class ScheduledStartHandler
{
    public static bool Started;

    public OrderTimeout Handle(PlaceOrder message)
    {
        Started = true;
        return new OrderTimeout(message.OrderId);
    }
}

public sealed class PlayedTimeoutHandler
{
    public static List<int> Handled { get; } = [];

    public void Handle(OrderTimeout message) => Handled.Add(message.OrderId);
}

public sealed class FlakyScheduledTimeoutHandler
{
    public static List<int> SeenAttempts { get; } = [];

    public void Handle(OrderTimeout message, Envelope envelope)
    {
        SeenAttempts.Add(envelope.Attempts.Value);
        throw new TimeoutException("not yet");
    }
}

public sealed class FailingThenPendingTimeoutHandler
{
    public static List<int> Seen { get; } = [];

    public void Handle(OrderTimeout message)
    {
        Seen.Add(message.OrderId);
        if (message.OrderId == 1)
        {
            throw new InvalidOperationException("first failed");
        }
    }
}

public sealed class ThrowingScheduledStartHandler
{
    public OrderTimeout Handle(PlaceOrder message)
    {
        _ = new OrderTimeout(message.OrderId);
        throw new TimeoutException("start failed");
    }
}
