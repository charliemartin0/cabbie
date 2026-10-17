using Cabbie.Application.Bus;
using Cabbie.Application.Cascades;
using Cabbie.Application.Scheduling;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Infrastructure.Scheduling;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Scheduling;

public sealed class OutgoingDispatcherTests
{
    [Fact]
    public void mixed_cascade_publishes_immediate_and_parks_timeout()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var cascades = new RecordingImmediate();
        var dispatcher = new OutgoingDispatcher(hold, cascades);
        var parent = EnvelopeFactory.Create(
            correlationId: new CorrelationId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            conversationId: new ConversationId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            sagaId: new SagaId("42"));

        dispatcher.Dispatch([new ChargePayment(1), new OrderTimeout(1)], parent);

        var payment = Assert.IsType<ChargePayment>(Assert.Single(cascades.Published));
        Assert.Equal(1, payment.OrderId);
        var parked = Assert.Single(hold.Peek());
        Assert.IsType<OrderTimeout>(parked.Message.Value);
        Assert.Equal(parent.ConversationId, parked.ConversationId);
        Assert.Equal(parent.CorrelationId, parked.CorrelationId);
        Assert.Equal(parent.SagaId, parked.SagaId);
        Assert.NotEqual(parent.Id, parked.Id);
        Assert.Equal(parked.SentAt.Value.AddMinutes(1), parked.DeliverBy.Value);
    }

    [Fact]
    public void empty_outgoing_parks_nothing()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var cascades = new RecordingImmediate();
        var dispatcher = new OutgoingDispatcher(hold, cascades);

        dispatcher.Dispatch([], EnvelopeFactory.Create());

        Assert.Empty(hold.Peek());
        Assert.Empty(cascades.Published);
    }

    [Fact]
    public void until_before_sent_at_fails_envelope_validation()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var dispatcher = new OutgoingDispatcher(hold);
        DateTimeOffset sent = DateTimeOffset.UtcNow;
        var options = new DeliveryOptions { Until = sent.AddMinutes(-1) };

        Assert.Throws<FluentValidation.ValidationException>(
            () => dispatcher.TryPark(new PlaceOrder(1), options));
        Assert.Empty(hold.Peek());
    }

    private sealed class RecordingImmediate : ICascadePublisher
    {
        public List<object> Published { get; } = [];

        public void Publish(IReadOnlyList<object> outgoing) => Published.AddRange(outgoing);
    }
}
