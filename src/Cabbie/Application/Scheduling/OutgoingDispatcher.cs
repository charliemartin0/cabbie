using Cabbie.Application.Bus;
using Cabbie.Application.Cascades;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.Validators;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Domain.Sagas.ValueObjects;

namespace Cabbie.Application.Scheduling;

/// <summary>
/// After handler success (and delayed Publish): park delayed envelopes, publish immediate bodies.
/// </summary>
public sealed class OutgoingDispatcher
{
    private static readonly EnvelopeValidator Validator = new();
    private static readonly Uri ScheduledDestination = new("local://scheduled/");

    private readonly ICascadePublisher? _immediate;
    private readonly IScheduledEnvelopeHold _hold;
    private readonly Action<object, Envelope>? _onImmediate;

    public OutgoingDispatcher(
        IScheduledEnvelopeHold hold,
        ICascadePublisher? immediate = null,
        Action<object, Envelope>? onImmediate = null)
    {
        ArgumentNullException.ThrowIfNull(hold);
        _hold = hold;
        _immediate = immediate;
        _onImmediate = onImmediate;
    }

    public bool TryPark(object message, DeliveryOptions? options, Envelope? parent = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        DateTimeOffset sentAt = DateTimeOffset.UtcNow;
        DateTimeOffset? due = DelayedDelivery.DueAt(message.GetType(), options, sentAt);
        if (due is null)
        {
            return false;
        }

        _hold.Park(Build(message, sentAt, due.Value, parent));
        return true;
    }

    public void Dispatch(IReadOnlyList<object> outgoing, Envelope parent)
    {
        ArgumentNullException.ThrowIfNull(outgoing);
        ArgumentNullException.ThrowIfNull(parent);
        var immediate = new List<object>();
        foreach (object item in outgoing)
        {
            (object message, DeliveryOptions? options) = UnwrapSchedule(item);
            if (!TryPark(message, options, parent))
            {
                immediate.Add(message);
            }
        }

        if (immediate.Count > 0)
        {
            if (_onImmediate is not null)
            {
                foreach (object message in immediate)
                {
                    _onImmediate(message, parent);
                }
            }

            _immediate?.Publish(immediate);
        }
    }

    private static (object Message, DeliveryOptions? Options) UnwrapSchedule(object item) =>
        item is ScheduledCascade scheduled
            ? (scheduled.Message, new DeliveryOptions { Until = scheduled.Until })
            : (item, null);

    private static Envelope Build(object message, DateTimeOffset sentAt, DateTimeOffset due, Envelope? parent)
    {
        Envelope envelope = new(
            new EnvelopeId(Guid.NewGuid()),
            new Message(message),
            MessageTypeNaming.For(message.GetType()),
            new Destination(ScheduledDestination),
            parent?.CorrelationId ?? new CorrelationId(Guid.NewGuid()),
            parent?.ConversationId ?? new ConversationId(Guid.NewGuid()),
            parent?.SagaId ?? new SagaId(""),
            new SentAt(sentAt),
            new DeliverBy(due),
            new Headers(),
            new ContentType(""),
            new Attempts(1),
            new EnvelopeData());

        var result = Validator.Validate(envelope);
        if (!result.IsValid)
        {
            throw new FluentValidation.ValidationException(result.Errors);
        }

        return envelope;
    }
}
