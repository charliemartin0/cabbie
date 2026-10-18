using Cabbie.Application.Bus;
using Cabbie.Application.Cascades;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Sagas;
using Cabbie.Application.Tracking;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Errors.ValueObjects;
using Cabbie.Domain.Sagas;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Infrastructure.Sagas;
using Cabbie.Tests.Application.Mediator;
using Cabbie.Tests.Application.Sagas;
using Cabbie.Tests.Application.Transports;
using Cabbie.Tests.Domain;

namespace Cabbie.Tests.Application.Tracking;

public sealed class TrackedSessionTests
{
    public TrackedSessionTests()
    {
        FlakyChargeThenPaidHandler.Seen.Clear();
        PaidWasHandled.Handled = false;
        ChargePaymentWasHandled.Handled = false;
        ConversationTimeoutHandler.Handled.Clear();
        BoomHandler.Handled = false;
        SiblingHandler.Handled = false;
        SecondHangHandler.Handled = false;
        HangUntilCancelledHandler.Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GateHandler.Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GateHandler.Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        NestedTrackHandler.Bus = null;
        NestedTrackHandler.InnerStarted = false;
        CascadePaidSaga.LastToken = "";
        ChainTimeoutHandler.Handled = false;
        ImmediatePublishHandler.Handled = false;
    }

    [Fact]
    public async Task tracked_invoke_runs_flaky_charge_three_times_then_paid_and_parks_timeout()
    {
        IMessageBus bus = ConversationBus();

        TrackedSession session = await bus.InvokeTrackedAsync(new PlaceOrder(1));

        Assert.Equal(1, session.Executed.Count(e => e.Message is PlaceOrder));
        Assert.Equal(3, session.Executed.Count(e => e.Message is ChargePayment));
        Assert.Equal(1, session.Executed.Count(e => e.Message is PaymentCharged));
        Assert.Equal([1, 2, 3], FlakyChargeThenPaidHandler.Seen);
        Assert.Contains(session.Published, p => p.Message is ChargePayment);
        Assert.Contains(session.Published, p => p.Message is PaymentCharged);
        Assert.DoesNotContain(session.Executed, e => e.Message is OrderTimeout);
        Assert.IsType<OrderTimeout>(Assert.Single(session.Scheduled).Envelope.Message.Value);
        Assert.True(PaidWasHandled.Handled);
    }

    [Fact]
    public async Task play_scheduled_executes_the_timeout_on_a_new_session()
    {
        IMessageBus bus = ConversationBus();
        TrackedSession first = await bus.InvokeTrackedAsync(new PlaceOrder(1));
        DateTimeOffset due = Assert.Single(first.Scheduled).Envelope.DeliverBy.Value!.Value;

        TrackedSession second = await first.PlayScheduledMessagesAsync(due);

        Assert.DoesNotContain(first.Executed, e => e.Message is OrderTimeout);
        Assert.Equal(1, second.Executed.Count(e => e.Message is OrderTimeout));
        Assert.DoesNotContain(second.Executed, e => e.Message is PlaceOrder);
        Assert.Equal([1], ConversationTimeoutHandler.Handled);
    }

    [Fact]
    public async Task untracked_invoke_still_does_not_run_cascaded_handlers()
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

    [Fact]
    public async Task nested_immediate_cascades_run_in_the_same_wait()
    {
        IMessageBus bus = ConversationBus();

        TrackedSession session = await bus.InvokeTrackedAsync(new PlaceOrder(1));

        Assert.Contains(session.Executed, e => e.Message is PaymentCharged);
        Assert.True(PaidWasHandled.Handled);
    }

    [Fact]
    public async Task drained_cascades_inherit_the_root_conversation_id()
    {
        IMessageBus bus = ConversationBus();

        TrackedSession session = await bus.InvokeTrackedAsync(new PlaceOrder(1));

        Guid conversation = session.Executed.First(e => e.Message is PlaceOrder).Envelope.ConversationId.Value;
        Assert.All(
            session.Executed.Where(e => e.Message is ChargePayment or PaymentCharged),
            e => Assert.Equal(conversation, e.Envelope.ConversationId.Value));
    }

    [Fact]
    public async Task descendant_handler_fault_stops_siblings_and_sets_session()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(StartBoomAndSiblingHandler));
        catalog.Scan(typeof(BoomHandler));
        catalog.Scan(typeof(SiblingHandler));
        var policies = new ErrorPolicyCatalog();
        policies.OnUnmatchedException();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, policies: policies);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => bus.InvokeTrackedAsync(new PlaceOrder(1)));

        Assert.NotNull(fault.Session);
        Assert.True(BoomHandler.Handled);
        Assert.False(SiblingHandler.Handled);
        Assert.Contains(fault.Session.Executed, e => e.Message is Boom);
    }

    [Fact]
    public async Task tracked_root_handler_fault_sets_session_with_attempts()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(AlwaysTimeoutPlaceOrderHandler));
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Retry().Retry().Then.MoveToErrorQueue();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, policies: policies);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => bus.InvokeTrackedAsync(new PlaceOrder(1)));

        Assert.NotNull(fault.Session);
        Assert.Equal(3, fault.Session.Executed.Count(e => e.Message is PlaceOrder));
        Assert.All(fault.Session.Executed, e => Assert.NotNull(e.Exception));
    }

    [Fact]
    public async Task cancel_during_drain_does_not_run_remaining_worklist_items()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(StartHangAndSecondHandler));
        catalog.Scan(typeof(HangUntilCancelledHandler));
        catalog.Scan(typeof(SecondHangHandler));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog);
        using var cts = new CancellationTokenSource();

        Task<TrackedSession> invoke = bus.InvokeTrackedAsync(new PlaceOrder(1), cts.Token);
        await HangUntilCancelledHandler.Started.Task;
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoke);
        Assert.False(SecondHangHandler.Handled);
    }

    [Fact]
    public async Task overlapping_tracked_invoke_throws_track_in_progress()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(GateHandler));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog);

        Task<TrackedSession> first = bus.InvokeTrackedAsync(new GateMessage());
        await GateHandler.Entered.Task;

        await Assert.ThrowsAsync<TrackInProgress>(() => bus.InvokeTrackedAsync(new GateMessage()));

        GateHandler.Release.SetResult();
        await first;
    }

    [Fact]
    public async Task handler_starting_a_tracked_session_throws_nested_track_not_supported()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(NestedTrackHandler));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog);
        NestedTrackHandler.Bus = bus;

        await Assert.ThrowsAsync<NestedTrackNotSupported>(() => bus.InvokeTrackedAsync(new PlaceOrder(1)));
        Assert.False(NestedTrackHandler.InnerStarted);
    }

    [Fact]
    public async Task delayed_tracked_invoke_is_still_delayed_invoke_not_supported()
    {
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(new HandlerCatalog());

        await Assert.ThrowsAsync<DelayedInvokeNotSupported>(
            () => bus.InvokeTrackedAsync(
                new PlaceOrder(1),
                new DeliveryOptions { Delay = TimeSpan.FromMinutes(1) }));
    }

    [Fact]
    public async Task tracked_publish_runs_an_immediate_root_then_until_quiet()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ImmediatePublishHandler));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog);

        TrackedSession session = await bus.PublishTrackedAsync(new ChargePayment(1));

        Assert.True(ImmediatePublishHandler.Handled);
        Assert.Contains(session.Published, p => p.Message is ChargePayment);
        Assert.Contains(session.Executed, e => e.Message is ChargePayment);
    }

    [Fact]
    public async Task tracked_publish_of_a_timeout_parks_and_does_not_execute()
    {
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(new HandlerCatalog());

        TrackedSession session = await bus.PublishTrackedAsync(new OrderTimeout(1));

        Assert.IsType<OrderTimeout>(Assert.Single(session.Scheduled).Envelope.Message.Value);
        Assert.Empty(session.Executed);
    }

    [Fact]
    public async Task untracked_publish_of_an_immediate_message_enqueues_without_a_worker()
    {
        var sender = new RecordingSender();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(
            new HandlerCatalog(),
            sender: sender);

        await bus.PublishAsync(new ChargePayment(1));

        Assert.IsType<ChargePayment>(Assert.Single(sender.Sent).Message.Value);
    }

    [Fact]
    public async Task empty_play_returns_a_new_session_with_empty_bags()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(RecordedIncidentHandler));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog);
        TrackedSession first = await bus.InvokeTrackedAsync(new RecordedIncident("ok"));

        TrackedSession second = await first.PlayScheduledMessagesAsync(DateTimeOffset.UtcNow);

        Assert.Empty(second.Executed);
        Assert.Empty(second.Published);
        Assert.Empty(second.Scheduled);
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task play_does_not_run_work_parked_during_the_play()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(StartTimeoutOnlyHandler));
        catalog.Scan(typeof(TimeoutChainsHandler));
        catalog.Scan(typeof(ChainTimeoutHandler));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog);
        TrackedSession first = await bus.InvokeTrackedAsync(new PlaceOrder(1));
        DateTimeOffset asOf = DateTimeOffset.UtcNow.AddMinutes(2);

        TrackedSession second = await first.PlayScheduledMessagesAsync(asOf);

        Assert.Contains(second.Executed, e => e.Message is OrderTimeout);
        Assert.False(ChainTimeoutHandler.Handled);
        Assert.Contains(second.Scheduled, s => s.Envelope.Message.Value is ChainTimeout);
    }

    [Fact]
    public async Task cascaded_schedule_retry_parks_instead_of_throwing()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(StartChargeOnlyHandler));
        catalog.Scan(typeof(AlwaysTimeoutChargeHandler));
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().ScheduleRetry(TimeSpan.FromMinutes(1));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, policies: policies);

        TrackedSession session = await bus.InvokeTrackedAsync(new PlaceOrder(1));

        Assert.Equal(1, session.Executed.Count(e => e.Message is ChargePayment));
        Assert.Contains(session.Scheduled, s => s.Envelope.Message.Value is ChargePayment);
        Assert.Equal(2, session.Scheduled.Single(s => s.Envelope.Message.Value is ChargePayment).Envelope.Attempts.Value);
    }

    [Fact]
    public async Task saga_handle_cascade_loads_the_same_instance_start_created()
    {
        var store = new InMemorySagaStore();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(CascadePaidSaga));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, sagas: store);

        await bus.InvokeTrackedAsync(new ConversationStarted(1));

        var saga = Assert.IsType<CascadePaidSaga>(store.Load(typeof(CascadePaidSaga), new SagaId("1")));
        Assert.Equal("paid-started", saga.Token);
        Assert.Equal("paid-started", CascadePaidSaga.LastToken);
    }

    private static IMessageBus ConversationBus()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(StartOrderHandler));
        catalog.Scan(typeof(FlakyChargeThenPaidHandler));
        catalog.Scan(typeof(PaidWasHandled));
        catalog.Scan(typeof(ConversationTimeoutHandler));
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Retry().Retry();
        return new Cabbie.Application.Mediator.Mediator(catalog, policies: policies);
    }
}

public sealed record PaymentCharged(int OrderId);

public sealed record Boom(int OrderId);

public sealed record Sibling(int OrderId);

public sealed record Hang(int OrderId);

public sealed record SecondHang(int OrderId);

public sealed record GateMessage;

[Timeout(Milliseconds = 1)]
public sealed record ChainTimeout(int OrderId);

public sealed class StartOrderHandler
{
    public object Handle(PlaceOrder message) =>
        new OutgoingMessages { new ChargePayment(message.OrderId), new OrderTimeout(message.OrderId) };
}

public sealed class FlakyChargeThenPaidHandler
{
    public static List<int> Seen { get; } = [];

    public PaymentCharged Handle(ChargePayment payment, Envelope envelope)
    {
        Seen.Add(envelope.Attempts.Value);
        if (envelope.Attempts.Value < 3)
        {
            throw new TimeoutException("Payment gateway timeout");
        }

        return new PaymentCharged(payment.OrderId);
    }
}

public sealed class PaidWasHandled
{
    public static bool Handled;

    public void Handle(PaymentCharged message) => Handled = true;
}

public sealed class ConversationTimeoutHandler
{
    public static List<int> Handled { get; } = [];

    public void Handle(OrderTimeout message) => Handled.Add(message.OrderId);
}

public sealed class StartBoomAndSiblingHandler
{
    public object Handle(PlaceOrder message) =>
        new OutgoingMessages { new Boom(message.OrderId), new Sibling(message.OrderId) };
}

public sealed class BoomHandler
{
    public static bool Handled;

    public void Handle(Boom message)
    {
        Handled = true;
        throw new InvalidOperationException("boom");
    }
}

public sealed class SiblingHandler
{
    public static bool Handled;

    public void Handle(Sibling message) => Handled = true;
}

public sealed class AlwaysTimeoutPlaceOrderHandler
{
    public void Handle(PlaceOrder message, Envelope envelope) =>
        throw new TimeoutException("always");
}

public sealed class StartHangAndSecondHandler
{
    public object Handle(PlaceOrder message) =>
        new OutgoingMessages { new Hang(message.OrderId), new SecondHang(message.OrderId) };
}

public sealed class HangUntilCancelledHandler
{
    public static TaskCompletionSource Started { get; set; } = new();

    public async Task Handle(Hang message, CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

public sealed class SecondHangHandler
{
    public static bool Handled;

    public void Handle(SecondHang message) => Handled = true;
}

public sealed class GateHandler
{
    public static TaskCompletionSource Entered { get; set; } = new();

    public static TaskCompletionSource Release { get; set; } = new();

    public async Task Handle(GateMessage message)
    {
        Entered.TrySetResult();
        await Release.Task;
    }
}

public sealed class NestedTrackHandler
{
    public static IMessageBus? Bus;

    public static bool InnerStarted;

    public void Handle(PlaceOrder message)
    {
        Bus!.InvokeTrackedAsync(new RecordedIncident("nested")).GetAwaiter().GetResult();
        InnerStarted = true;
    }
}

public sealed class StartTimeoutOnlyHandler
{
    public OrderTimeout Handle(PlaceOrder message) => new(message.OrderId);
}

public sealed class TimeoutChainsHandler
{
    public ChainTimeout Handle(OrderTimeout message) => new(message.OrderId);
}

public sealed class ChainTimeoutHandler
{
    public static bool Handled;

    public void Handle(ChainTimeout message) => Handled = true;
}

public sealed class StartChargeOnlyHandler
{
    public ChargePayment Handle(PlaceOrder message) => new(message.OrderId);
}

public sealed class AlwaysTimeoutChargeHandler
{
    public void Handle(ChargePayment payment, Envelope envelope) =>
        throw new TimeoutException("always");
}

public sealed class ImmediatePublishHandler
{
    public static bool Handled;

    public void Handle(ChargePayment payment) => Handled = true;
}

public sealed class CascadePaidSaga : Saga
{
    public static string LastToken = "";

    public string Token { get; set; } = "";

    public object Start(ConversationStarted message)
    {
        Token = "started";
        return new ConversationPaid(message.Id);
    }

    public void Handle(ConversationPaid message)
    {
        Token = "paid-" + Token;
        LastToken = Token;
    }
}
