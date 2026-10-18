using Cabbie.Application.Routing;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.Validators;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Domain.Sagas.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Cabbie.EntityFrameworkCore;

/// <summary>
/// Stages outgoing envelopes in the caller's <typeparamref name="TContext"/>. Nothing is written until the
/// caller's own <c>SaveChanges</c>, so the rows commit or roll back with the caller's data.
/// </summary>
public sealed class EfOutbox<TContext>
    where TContext : DbContext
{
    private static readonly EnvelopeValidator Validator = new();

    private readonly TContext _context;
    private readonly EnvelopeCodec _codec;
    private readonly RoutingCatalog _routing;
    private readonly TimeProvider _time;

    public EfOutbox(TContext context, EnvelopeCodec codec, RoutingCatalog routing, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(routing);
        _context = context;
        _codec = codec;
        _routing = routing;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Add an envelope to the context's change tracker. Does not save.</summary>
    public void Stage(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _context.Set<OutboxMessage>().Add(OutboxMessageMapper.ToRow(envelope, _codec, _time.GetUtcNow()));
    }

    /// <summary>Wrap <paramref name="message"/> in a fresh envelope routed by the routing catalog and stage it.</summary>
    public void Publish(object message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Envelope envelope = new(
            new EnvelopeId(Guid.NewGuid()),
            new Message(message),
            MessageTypeNaming.For(message.GetType()),
            _routing.For(message),
            new CorrelationId(Guid.NewGuid()),
            new ConversationId(Guid.NewGuid()),
            new SagaId(""),
            new SentAt(_time.GetUtcNow()),
            new DeliverBy(null),
            new Headers(),
            new ContentType(""),
            new Attempts(1),
            new EnvelopeData());

        FluentValidation.Results.ValidationResult validation = Validator.Validate(envelope);
        if (!validation.IsValid)
        {
            throw new FluentValidation.ValidationException(validation.Errors);
        }

        Stage(envelope);
    }
}
