using Cabbie.Application.Bus;
using Cabbie.Application.Cascades;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Scheduling;
using Cabbie.Application.Tracking;
using Cabbie.Domain.Envelope;
using Cabbie.Infrastructure.Scheduling;

namespace Cabbie.Tests.Application.Bus;

/// <summary>
/// Prove-with for the ScheduledCascade API: a handler returns an immediate cascade
/// plus a per-message absolute schedule; DispatchOutgoing parks the wrapper's message
/// in the scheduled hold at the wrapper's Until, and PlayDue fires it later.
/// </summary>
public sealed class ScheduledCascadeTests
{
    [Fact]
    public async Task handler_returning_immediate_and_scheduled_cascade_parks_at_explicit_until()
    {
        IMessageBus bus = Bus();

        TrackedSession session = await bus.InvokeTrackedAsync(new TriggerSchedule(1));

        // The immediate cascade ran in the parent session.
        Assert.Contains(session.Executed, executed => executed.Message is ChargePayment);

        // The scheduled cascade parked exactly one envelope, at the explicit until.
        ScheduledRecord scheduled = Assert.Single(session.Scheduled);
        Envelope parked = scheduled.Envelope;
        Assert.IsType<Reminder>(parked.Message.Value);
        Assert.Equal("local://scheduled/", parked.Destination.Value.ToString());
        Assert.InRange(
            parked.DeliverBy.Value!.Value,
            DateTimeOffset.UtcNow.AddSeconds(1),
            DateTimeOffset.UtcNow.AddSeconds(3));

        // Conversation and correlation inherited from the parent envelope.
        Envelope parent = session.Executed.First(executed => executed.Message is TriggerSchedule).Envelope;
        Assert.Equal(parent.ConversationId, parked.ConversationId);
        Assert.Equal(parent.CorrelationId, parked.CorrelationId);
    }

    [Fact]
    public async Task play_due_fires_the_scheduled_cascade_at_its_until()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        IMessageBus bus = Bus(hold);

        TrackedSession first = await bus.InvokeTrackedAsync(new TriggerSchedule(1));
        DateTimeOffset until = Assert.Single(first.Scheduled).Envelope.DeliverBy.Value!.Value;

        TrackedSession second = await first.PlayScheduledMessagesAsync(until);

        Assert.Contains(second.Executed, executed => executed.Message is Reminder);
        Assert.Empty(hold.Peek());
    }

    [Fact]
    public async Task past_until_parks_silently_and_the_next_play_due_fires_it()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        IMessageBus bus = Bus(hold);

        TrackedSession first = await bus.InvokeTrackedAsync(new PastTrigger(1));

        // A past Until does not throw on the cascade path — it parks, and the next
        // PlayDue fires it immediately.
        ScheduledRecord scheduled = Assert.Single(first.Scheduled);
        Assert.True(scheduled.Envelope.DeliverBy.Value!.Value < DateTimeOffset.UtcNow);

        TrackedSession second = await first.PlayScheduledMessagesAsync(DateTimeOffset.UtcNow);

        Assert.Contains(second.Executed, executed => executed.Message is Reminder);
        Assert.Empty(hold.Peek());
    }

    private static IMessageBus Bus() => Bus(new InMemoryScheduledEnvelopeHold());

    private static IMessageBus Bus(IScheduledEnvelopeHold hold)
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(TriggerHandler));
        catalog.Scan(typeof(PastTriggerHandler));
        catalog.Scan(typeof(ChargePaymentHandler));
        catalog.Scan(typeof(ReminderHandler));
        return new Cabbie.Application.Mediator.Mediator(catalog, hold: hold);
    }
}

public sealed record TriggerSchedule(int ParentId);

public sealed class TriggerHandler
{
    public object[] Handle(TriggerSchedule message) =>
    [
        new ChargePayment(message.ParentId),
        new ScheduledCascade(new Reminder(message.ParentId), DateTimeOffset.UtcNow.AddSeconds(2))
    ];
}

public sealed record PastTrigger(int ParentId);

public sealed class PastTriggerHandler
{
    public ScheduledCascade Handle(PastTrigger message) =>
        new(new Reminder(message.ParentId), DateTimeOffset.UtcNow.AddSeconds(-10));
}

/// <summary>
/// No [Timeout] attribute — if the wrapper were ignored, this would dispatch
/// immediately instead of parking, and the tests would fail.
/// </summary>
public sealed record ChargePayment(int ParentId);

public sealed class ChargePaymentHandler
{
    public void Handle(ChargePayment message)
    {
    }
}

public sealed record Reminder(int ParentId);

public sealed class ReminderHandler
{
    public void Handle(Reminder message)
    {
    }
}