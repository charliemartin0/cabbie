using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Domain.Sagas.ValueObjects;

namespace Cabbie.Domain.Envelope;

public record Envelope(
    EnvelopeId Id,
    Message Message,
    MessageType MessageType,
    Destination Destination,
    CorrelationId CorrelationId,
    ConversationId ConversationId,
    SagaId SagaId,
    SentAt SentAt,
    DeliverBy DeliverBy,
    Headers Headers,
    ContentType ContentType,
    Attempts Attempts,
    EnvelopeData Data,
    CausationId? CausationId = null);
