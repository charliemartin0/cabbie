using System.Text.Json;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;

namespace Cabbie.EntityFrameworkCore;

internal static class OutboxMessageMapper
{
    public static OutboxMessage ToRow(Envelope envelope, EnvelopeCodec codec, DateTimeOffset createdAt)
    {
        TransportMessage message = codec.Encode(envelope);
        return new OutboxMessage
        {
            Id = envelope.Id.Value,
            Destination = envelope.Destination.Value.ToString(),
            MessageType = envelope.MessageType.Value,
            Headers = JsonSerializer.Serialize(message.Headers),
            Body = message.Body.ToArray(),
            CreatedAt = createdAt,
        };
    }

    /// <exception cref="UnreadableTransportMessage">The row cannot be rebuilt by this host.</exception>
    public static Envelope ToEnvelope(OutboxMessage row, EnvelopeCodec codec)
    {
        Dictionary<string, string>? headers;
        try
        {
            headers = JsonSerializer.Deserialize<Dictionary<string, string>>(row.Headers);
        }
        catch (JsonException exception)
        {
            throw new UnreadableTransportMessage($"Outbox row {row.Id} has unreadable headers.", exception);
        }

        return codec.Decode(new TransportMessage(headers ?? [], row.Body));
    }
}
