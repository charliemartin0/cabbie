namespace Cabbie.Application.Transports;

/// <summary>
/// Names of the reserved headers <see cref="EnvelopeCodec"/> writes for envelope fields.
/// Custom envelope headers may not start with <see cref="Prefix"/>.
/// <para>
/// Wire transports with attribute limits (SQS allows 10 message attributes per message) must
/// pack the <c>cabbie-*</c> headers, for example into one attribute or the body wrapper, and
/// unpack them before <see cref="EnvelopeCodec.Decode"/>, rather than map them one to one.
/// </para>
/// </summary>
public static class EnvelopeHeaders
{
    public const string Prefix = "cabbie-";
    public const string MessageId = "cabbie-message-id";
    public const string MessageType = "cabbie-message-type";
    public const string Destination = "cabbie-destination";
    public const string CorrelationId = "cabbie-correlation-id";
    public const string ConversationId = "cabbie-conversation-id";
    public const string CausationId = "cabbie-causation-id";
    public const string SagaId = "cabbie-saga-id";
    public const string SentAt = "cabbie-sent-at";
    public const string DeliverBy = "cabbie-deliver-by";
    public const string Attempts = "cabbie-attempts";
    public const string ContentType = "cabbie-content-type";
    public const string Handler = "cabbie-handler";
}
