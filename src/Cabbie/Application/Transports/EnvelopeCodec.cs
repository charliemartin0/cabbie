using System.Globalization;
using Cabbie.Application.Serialization;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Domain.Sagas.ValueObjects;

namespace Cabbie.Application.Transports;

/// <summary>
/// Maps an Envelope to a <see cref="TransportMessage"/> (<c>cabbie-*</c> headers plus a
/// body serialized by <see cref="IMessageSerializer"/>) and back. This is the only place
/// the serializer runs: wire transports call it when sending and receiving; the in-process
/// local:// transport skips it unless given a codec. Decoding rejects anything this host
/// cannot faithfully rebuild with <see cref="UnreadableTransportMessage"/>.
/// </summary>
public sealed class EnvelopeCodec
{
    private readonly IMessageSerializer _serializer;
    private readonly MessageTypeCatalog _messageTypes;

    public EnvelopeCodec(IMessageSerializer serializer, MessageTypeCatalog messageTypes)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(messageTypes);
        _serializer = serializer;
        _messageTypes = messageTypes;
    }

    public TransportMessage Encode(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in envelope.Headers.Value)
        {
            if (key.StartsWith(EnvelopeHeaders.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Header '{key}' uses the reserved 'cabbie-' prefix.", nameof(envelope));
            }

            headers.Add(key, value);
        }

        headers[EnvelopeHeaders.MessageId] = envelope.Id.Value.ToString("D");
        headers[EnvelopeHeaders.MessageType] = envelope.MessageType.Value;
        headers[EnvelopeHeaders.Destination] = envelope.Destination.Value.ToString();
        headers[EnvelopeHeaders.CorrelationId] = envelope.CorrelationId.Value.ToString("D");
        headers[EnvelopeHeaders.ConversationId] = envelope.ConversationId.Value.ToString("D");
        headers[EnvelopeHeaders.SentAt] = envelope.SentAt.Value.ToString("O", CultureInfo.InvariantCulture);
        headers[EnvelopeHeaders.Attempts] = envelope.Attempts.Value.ToString(CultureInfo.InvariantCulture);
        headers[EnvelopeHeaders.ContentType] = _serializer.ContentType;
        if (envelope.CausationId is { } causation)
        {
            headers[EnvelopeHeaders.CausationId] = causation.Value.ToString("D");
        }

        if (envelope.SagaId.Value.Length > 0)
        {
            headers[EnvelopeHeaders.SagaId] = envelope.SagaId.Value;
        }

        if (envelope.DeliverBy.Value is { } deliverBy)
        {
            headers[EnvelopeHeaders.DeliverBy] = deliverBy.ToString("O", CultureInfo.InvariantCulture);
        }

        object message = envelope.Message.Value;
        return new TransportMessage(headers, _serializer.Serialize(message, message.GetType()));
    }

    public Envelope Decode(TransportMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        IReadOnlyDictionary<string, string> headers =
            new Dictionary<string, string>(message.Headers, StringComparer.OrdinalIgnoreCase);

        Guid id = ParseGuid(headers, EnvelopeHeaders.MessageId);
        string typeName = Required(headers, EnvelopeHeaders.MessageType);
        string rawDestination = Required(headers, EnvelopeHeaders.Destination);
        Uri destination = Uri.TryCreate(rawDestination, UriKind.Absolute, out Uri? parsedDestination)
            ? parsedDestination
            : throw Invalid(EnvelopeHeaders.Destination, rawDestination);
        Guid correlationId = ParseGuid(headers, EnvelopeHeaders.CorrelationId);
        Guid conversationId = ParseGuid(headers, EnvelopeHeaders.ConversationId);
        DateTimeOffset sentAt = ParseTime(Required(headers, EnvelopeHeaders.SentAt), EnvelopeHeaders.SentAt);
        string rawAttempts = Required(headers, EnvelopeHeaders.Attempts);
        int attempts = int.TryParse(rawAttempts, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedAttempts)
            && parsedAttempts >= 1
            ? parsedAttempts
            : throw Invalid(EnvelopeHeaders.Attempts, rawAttempts);

        if (headers.TryGetValue(EnvelopeHeaders.ContentType, out string? contentType)
            && contentType != _serializer.ContentType)
        {
            throw new UnreadableTransportMessage(
                $"Content type '{contentType}' is not supported; expected '{_serializer.ContentType}'.");
        }

        CausationId? causationId = headers.ContainsKey(EnvelopeHeaders.CausationId)
            ? new CausationId(ParseGuid(headers, EnvelopeHeaders.CausationId))
            : null;
        DateTimeOffset? deliverBy = headers.TryGetValue(EnvelopeHeaders.DeliverBy, out string? rawDeliverBy)
            ? ParseTime(rawDeliverBy, EnvelopeHeaders.DeliverBy)
            : null;
        string sagaId = headers.TryGetValue(EnvelopeHeaders.SagaId, out string? rawSaga) ? rawSaga : "";

        var messageType = new MessageType(typeName);
        if (_messageTypes.Lookup(messageType) is not KnownMessageType known)
        {
            throw new UnreadableTransportMessage($"Message type '{typeName}' is not known to this host.");
        }

        object body;
        try
        {
            body = _serializer.Deserialize(message.Body, known.ClrType);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new UnreadableTransportMessage($"Body is not a valid '{typeName}': {exception.Message}");
        }

        var custom = headers
            .Where(pair => !pair.Key.StartsWith(EnvelopeHeaders.Prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return new Envelope(
            new EnvelopeId(id),
            new Message(body),
            messageType,
            new Destination(destination),
            new CorrelationId(correlationId),
            new ConversationId(conversationId),
            new SagaId(sagaId),
            new SentAt(sentAt),
            new DeliverBy(deliverBy),
            new Headers(custom),
            new ContentType(_serializer.ContentType),
            new Attempts(attempts),
            new EnvelopeData(message.Body),
            causationId);
    }

    private static string Required(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out string? value)
            ? value
            : throw new UnreadableTransportMessage($"Header '{name}' is missing.");

    private static Guid ParseGuid(IReadOnlyDictionary<string, string> headers, string name)
    {
        string value = Required(headers, name);
        return Guid.TryParse(value, out Guid parsed) ? parsed : throw Invalid(name, value);
    }

    private static DateTimeOffset ParseTime(string value, string name) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : throw Invalid(name, value);

    private static UnreadableTransportMessage Invalid(string name, string value) =>
        new($"Header '{name}' has invalid value '{value}'.");
}
