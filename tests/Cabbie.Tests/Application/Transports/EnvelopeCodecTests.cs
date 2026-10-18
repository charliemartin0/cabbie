using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Infrastructure.Serialization;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Transports;

public sealed class EnvelopeCodecTests
{
    [Fact]
    public void encode_then_decode_preserves_ids_headers_attempts_and_body()
    {
        EnvelopeCodec codec = Codec();
        Envelope original = EnvelopeFactory.Create(
            message: new Message(new PlaceOrder(7)),
            destination: new Destination(new Uri("sqs://orders/")),
            sagaId: new SagaId("saga-1"),
            deliverBy: DateTimeOffset.UtcNow.AddMinutes(5),
            headers: new Headers(new Dictionary<string, string> { ["trace-id"] = "abc-123" }),
            attempts: 3,
            causationId: new CausationId(Guid.NewGuid()));

        TransportMessage wire = codec.Encode(original);
        Envelope decoded = codec.Decode(wire);

        Assert.Equal(original.Id, decoded.Id);
        Assert.Equal(original.MessageType, decoded.MessageType);
        Assert.Equal(original.Destination, decoded.Destination);
        Assert.Equal(original.CorrelationId, decoded.CorrelationId);
        Assert.Equal(original.ConversationId, decoded.ConversationId);
        Assert.Equal(original.CausationId, decoded.CausationId);
        Assert.Equal(original.SagaId, decoded.SagaId);
        Assert.Equal(original.SentAt, decoded.SentAt);
        Assert.Equal(original.DeliverBy, decoded.DeliverBy);
        Assert.Equal(original.Headers, decoded.Headers);
        Assert.Equal(3, decoded.Attempts.Value);
        Assert.Equal(new PlaceOrder(7), decoded.Message.Value);
        Assert.Equal("application/json", decoded.ContentType.Value);
        Assert.Equal(new EnvelopeData(wire.Body), decoded.Data);
    }

    [Fact]
    public void encode_then_decode_of_a_root_envelope_keeps_causation_saga_and_deadline_empty()
    {
        EnvelopeCodec codec = Codec();
        Envelope original = EnvelopeFactory.Create(message: new Message(new PlaceOrder(1)));

        Envelope decoded = codec.Decode(codec.Encode(original));

        Assert.Null(decoded.CausationId);
        Assert.Equal(new SagaId(""), decoded.SagaId);
        Assert.Equal(new DeliverBy(null), decoded.DeliverBy);
    }

    [Fact]
    public void decode_of_a_message_type_this_host_does_not_know_throws_unreadable_transport_message()
    {
        EnvelopeCodec codec = Codec();
        TransportMessage wire = codec.Encode(EnvelopeFactory.Create());
        var headers = new Dictionary<string, string>(wire.Headers)
        {
            [EnvelopeHeaders.MessageType] = "no-such-message",
        };

        UnreadableTransportMessage error = Assert.Throws<UnreadableTransportMessage>(
            () => codec.Decode(wire with { Headers = headers }));

        Assert.Contains("no-such-message", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void decode_without_a_message_id_header_throws_unreadable_transport_message()
    {
        EnvelopeCodec codec = Codec();
        TransportMessage wire = codec.Encode(EnvelopeFactory.Create());
        var headers = new Dictionary<string, string>(wire.Headers);
        headers.Remove(EnvelopeHeaders.MessageId);

        UnreadableTransportMessage error = Assert.Throws<UnreadableTransportMessage>(
            () => codec.Decode(wire with { Headers = headers }));

        Assert.Contains(EnvelopeHeaders.MessageId, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void decode_with_a_content_type_the_serializer_does_not_read_throws_unreadable_transport_message()
    {
        EnvelopeCodec codec = Codec();
        TransportMessage wire = codec.Encode(EnvelopeFactory.Create());
        var headers = new Dictionary<string, string>(wire.Headers)
        {
            [EnvelopeHeaders.ContentType] = "application/xml",
        };

        UnreadableTransportMessage error = Assert.Throws<UnreadableTransportMessage>(
            () => codec.Decode(wire with { Headers = headers }));

        Assert.Contains("application/xml", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void encode_rejects_a_custom_header_with_the_reserved_prefix()
    {
        EnvelopeCodec codec = Codec();
        Envelope envelope = EnvelopeFactory.Create(
            headers: new Headers(new Dictionary<string, string> { ["cabbie-message-id"] = "spoofed" }));

        Assert.Throws<ArgumentException>(() => codec.Encode(envelope));
    }

    [Fact]
    public void decode_wraps_any_serializer_exception_and_keeps_it_as_the_inner_exception()
    {
        var messageTypes = new MessageTypeCatalog();
        messageTypes.Register(typeof(PlaceOrder));
        var failure = new NotSupportedException("custom serializer failure");
        var codec = new EnvelopeCodec(new ThrowingSerializer(failure), messageTypes);
        TransportMessage wire = Codec().Encode(EnvelopeFactory.Create());

        UnreadableTransportMessage error = Assert.Throws<UnreadableTransportMessage>(() => codec.Decode(wire));

        Assert.Same(failure, error.InnerException);
    }

    private sealed class ThrowingSerializer(Exception failure) : Cabbie.Application.Serialization.IMessageSerializer
    {
        public string ContentType => "application/json";

        public byte[] Serialize(object message, Type messageType) => [];

        public object Deserialize(ReadOnlyMemory<byte> data, Type messageType) => throw failure;
    }

    [Fact]
    public void decode_of_a_body_that_does_not_match_the_message_type_throws_unreadable_transport_message()
    {
        EnvelopeCodec codec = Codec();
        TransportMessage wire = codec.Encode(EnvelopeFactory.Create());

        Assert.Throws<UnreadableTransportMessage>(
            () => codec.Decode(wire with { Body = "not json"u8.ToArray() }));
    }

    [Fact]
    public void decode_with_attempts_below_one_throws_unreadable_transport_message()
    {
        EnvelopeCodec codec = Codec();
        TransportMessage wire = codec.Encode(EnvelopeFactory.Create());
        var headers = new Dictionary<string, string>(wire.Headers) { [EnvelopeHeaders.Attempts] = "0" };

        Assert.Throws<UnreadableTransportMessage>(() => codec.Decode(wire with { Headers = headers }));
    }

    [Fact]
    public void decode_reads_reserved_headers_regardless_of_name_case()
    {
        EnvelopeCodec codec = Codec();
        Envelope original = EnvelopeFactory.Create();
        TransportMessage wire = codec.Encode(original);
        var upper = wire.Headers.ToDictionary(pair => pair.Key.ToUpperInvariant(), pair => pair.Value);

        Envelope decoded = codec.Decode(wire with { Headers = upper });

        Assert.Equal(original.Id, decoded.Id);
    }

    private static EnvelopeCodec Codec()
    {
        var messageTypes = new MessageTypeCatalog();
        messageTypes.Register(typeof(PlaceOrder));
        return new EnvelopeCodec(new JsonMessageSerializer(), messageTypes);
    }
}
