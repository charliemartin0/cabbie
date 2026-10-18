using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Scheduling;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Infrastructure.Scheduling;
using Cabbie.Tests.Application.Transports;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Bus;

/// <summary>
/// Causation and correlation ids on outgoing envelopes: a cascade is caused by the envelope
/// whose handler returned it and shares its correlation; a top-level send starts a new chain.
/// </summary>
public sealed class CausationTests
{
    [Fact]
    public async Task mediator_cascade_carries_the_parent_id_as_causation_and_the_parent_correlation()
    {
        CausePingHandler.Seen = null;
        var sender = new RecordingSender();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(Catalog<CausePingHandler>(), sender: sender);

        await bus.InvokeAsync(new CausePing(1));

        Envelope parent = Assert.IsType<Envelope>(CausePingHandler.Seen);
        Envelope child = Assert.Single(sender.Sent);
        Assert.IsType<CauseEffect>(child.Message.Value);
        Assert.Equal(new CausationId(parent.Id.Value), child.CausationId);
        Assert.Equal(parent.CorrelationId, child.CorrelationId);
        Assert.Null(parent.CausationId);
    }

    [Fact]
    public async Task mediator_top_level_publishes_each_start_a_new_correlation_with_no_causation()
    {
        var sender = new RecordingSender();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(new HandlerCatalog(), sender: sender);

        await bus.PublishAsync(new CausePing(1));
        await bus.PublishAsync(new CausePing(2));

        Assert.Equal(2, sender.Sent.Count);
        Assert.All(sender.Sent, envelope => Assert.Null(envelope.CausationId));
        Assert.All(sender.Sent, envelope => Assert.NotEqual(Guid.Empty, envelope.CorrelationId.Value));
        Assert.NotEqual(sender.Sent[0].CorrelationId, sender.Sent[1].CorrelationId);
    }

    [Fact]
    public async Task mediator_delayed_top_level_publish_is_parked_with_no_causation_and_a_new_correlation()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(new HandlerCatalog(), hold: hold, sender: new RecordingSender());

        await bus.PublishAsync(new CausePing(1), new DeliveryOptions { Delay = TimeSpan.FromMinutes(1) });

        Envelope parked = Assert.Single(hold.Peek());
        Assert.Null(parked.CausationId);
        Assert.NotEqual(Guid.Empty, parked.CorrelationId.Value);
    }

    [Fact]
    public async Task message_delivery_cascade_carries_the_parent_id_as_causation_and_the_parent_correlation()
    {
        var sender = new RecordingSender();
        MessageDelivery delivery = DeliveryFor<CausePingHandler>(new InMemoryScheduledEnvelopeHold(), sender);
        Envelope parent = ParentFor(new CausePing(1));

        await delivery.Dispatch(parent);

        Envelope child = Assert.Single(sender.Sent);
        Assert.Equal(new CausationId(parent.Id.Value), child.CausationId);
        Assert.Equal(parent.CorrelationId, child.CorrelationId);
    }

    [Fact]
    public async Task message_delivery_scheduled_cascade_is_parked_with_the_parent_as_causation()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        MessageDelivery delivery = DeliveryFor<CauseTickHandler>(hold, new RecordingSender());
        Envelope parent = ParentFor(new CauseTick(1));

        await delivery.Dispatch(parent);

        Envelope parked = Assert.Single(hold.Peek());
        Assert.IsType<OrderTimeout>(parked.Message.Value);
        Assert.Equal(new CausationId(parent.Id.Value), parked.CausationId);
        Assert.Equal(parent.CorrelationId, parked.CorrelationId);
    }

    [Fact]
    public void outgoing_dispatcher_parks_with_the_parent_as_causation_or_none_for_a_top_level_message()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var dispatcher = new OutgoingDispatcher(hold);
        Envelope parent = EnvelopeFactory.Create();

        dispatcher.TryPark(new OrderTimeout(1), null, parent);
        dispatcher.TryPark(new OrderTimeout(2), null);

        IReadOnlyList<Envelope> parked = hold.Peek();
        Assert.Equal(new CausationId(parent.Id.Value), parked[0].CausationId);
        Assert.Equal(parent.CorrelationId, parked[0].CorrelationId);
        Assert.Null(parked[1].CausationId);
        Assert.NotEqual(parent.CorrelationId, parked[1].CorrelationId);
    }

    private static HandlerCatalog Catalog<THandler>()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(THandler));
        return catalog;
    }

    private static MessageDelivery DeliveryFor<THandler>(IScheduledEnvelopeHold hold, RecordingSender sender) =>
        new(Catalog<THandler>(), new Executor(new ErrorPolicyCatalog()), hold: hold, sender: sender);

    private static Envelope ParentFor(object message) =>
        EnvelopeFactory.Create(
            message: new Message(message),
            messageType: MessageTypeNaming.For(message.GetType()));
}

public sealed record CausePing(int Id);

public sealed record CauseEffect(int Id);

public sealed record CauseTick(int Id);

public sealed class CausePingHandler
{
    public static Envelope? Seen { get; set; }

    public CauseEffect Handle(CausePing message, Envelope envelope)
    {
        Seen = envelope;
        return new CauseEffect(message.Id);
    }
}

public sealed class CauseTickHandler
{
    public OrderTimeout Handle(CauseTick message) => new(message.Id);
}
