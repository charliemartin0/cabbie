using Amazon.SQS.Model;
using Cabbie.Application.Transports;

namespace Cabbie.Aws.Tests;

public sealed class SqsWireTests
{
    [Fact]
    public void thirty_headers_travel_in_one_attribute_and_come_back_intact()
    {
        Dictionary<string, string> headers = Enumerable.Range(1, 30).ToDictionary(i => $"x-{i}", i => $"value {i}");
        headers["cabbie-message-id"] = "abc";
        var message = new TransportMessage(headers, "{}"u8.ToArray());

        (string body, Dictionary<string, MessageAttributeValue> attributes) = SqsWire.Pack(message);
        TransportMessage unpacked = SqsWire.Unpack(body, attributes);

        Assert.Single(attributes);
        Assert.Equal(headers, unpacked.Headers);
        Assert.Equal("{}"u8.ToArray(), unpacked.Body.ToArray());
    }

    [Fact]
    public void bytes_that_are_not_utf8_travel_as_flagged_base64()
    {
        byte[] bytes = [0xFF, 0xFE, 0x00, 0x10];

        (string body, Dictionary<string, MessageAttributeValue> attributes) =
            SqsWire.Pack(new TransportMessage(new Dictionary<string, string>(), bytes));
        TransportMessage unpacked = SqsWire.Unpack(body, attributes);

        Assert.Equal("base64", attributes[SqsWire.BodyEncodingAttribute].StringValue);
        Assert.Equal(bytes, unpacked.Body.ToArray());
    }

    [Fact]
    public void text_with_characters_sqs_rejects_travels_as_flagged_base64()
    {
        byte[] bytes = "a\u0001b"u8.ToArray();

        (string body, Dictionary<string, MessageAttributeValue> attributes) =
            SqsWire.Pack(new TransportMessage(new Dictionary<string, string>(), bytes));

        Assert.Contains(SqsWire.BodyEncodingAttribute, attributes.Keys);
        Assert.Equal(bytes, SqsWire.Unpack(body, attributes).Body.ToArray());
    }

    [Fact]
    public void plain_text_is_sent_as_is_without_an_encoding_attribute()
    {
        byte[] bytes = "{\"n\":\"東京\"}"u8.ToArray();

        (string body, Dictionary<string, MessageAttributeValue> attributes) =
            SqsWire.Pack(new TransportMessage(new Dictionary<string, string>(), bytes));

        Assert.Equal("{\"n\":\"東京\"}", body);
        Assert.DoesNotContain(SqsWire.BodyEncodingAttribute, attributes.Keys);
    }

    [Fact]
    public void an_empty_body_cannot_be_packed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SqsWire.Pack(new TransportMessage(new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    public void a_message_without_the_headers_attribute_is_unreadable()
    {
        Assert.Throws<UnreadableTransportMessage>(() => SqsWire.Unpack("{}", new Dictionary<string, MessageAttributeValue>()));
    }

    [Fact]
    public void headers_that_are_not_a_json_object_of_strings_are_unreadable()
    {
        foreach (string raw in new[] { "[1]", "{\"a\":1}", "not json", "null" })
        {
            var attributes = new Dictionary<string, MessageAttributeValue>
            {
                [SqsWire.HeadersAttribute] = new() { DataType = "String", StringValue = raw },
            };

            Assert.Throws<UnreadableTransportMessage>(() => SqsWire.Unpack("{}", attributes));
        }
    }

    [Fact]
    public void an_unknown_body_encoding_is_unreadable()
    {
        var attributes = new Dictionary<string, MessageAttributeValue>
        {
            [SqsWire.HeadersAttribute] = new() { DataType = "String", StringValue = "{}" },
            [SqsWire.BodyEncodingAttribute] = new() { DataType = "String", StringValue = "rot13" },
        };

        Assert.Throws<UnreadableTransportMessage>(() => SqsWire.Unpack("{}", attributes));
    }
}
