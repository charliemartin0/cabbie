using System.Text;
using System.Text.Json;
using Amazon.SQS.Model;
using Cabbie.Application.Transports;

namespace Cabbie.Aws;

/// <summary>
/// The only place a <see cref="TransportMessage"/> is mapped onto an SQS message. SQS allows 10
/// message attributes, so every header (reserved <c>cabbie-*</c> and custom) travels as one JSON
/// object in <see cref="HeadersAttribute"/>. The body is sent as text when it is valid SQS text and
/// as base64 (flagged by <see cref="BodyEncodingAttribute"/>) otherwise.
/// </summary>
internal static class SqsWire
{
    public const string HeadersAttribute = "cabbie-headers";
    public const string BodyEncodingAttribute = "cabbie-body-encoding";
    public const string DeadLetterReasonAttribute = "cabbie-dead-letter-reason";
    public const int MaxAttributes = 10;

    private const string Base64 = "base64";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static (string Body, Dictionary<string, MessageAttributeValue> Attributes) Pack(TransportMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Body.IsEmpty)
        {
            throw new InvalidOperationException("SQS cannot carry an empty message body.");
        }

        var attributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
        {
            [HeadersAttribute] = StringAttribute(JsonSerializer.Serialize(message.Headers)),
        };

        string? text = TryText(message.Body.Span);
        if (text is null)
        {
            attributes[BodyEncodingAttribute] = StringAttribute(Base64);
            text = Convert.ToBase64String(message.Body.Span);
        }

        return (text, attributes);
    }

    public static TransportMessage Unpack(string body, IReadOnlyDictionary<string, MessageAttributeValue>? attributes)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (attributes is null
            || !attributes.TryGetValue(HeadersAttribute, out MessageAttributeValue? headersAttribute)
            || headersAttribute.StringValue is not { } rawHeaders)
        {
            throw new UnreadableTransportMessage($"SQS message attribute '{HeadersAttribute}' is missing or not a string.");
        }

        Dictionary<string, string>? headers;
        try
        {
            headers = JsonSerializer.Deserialize<Dictionary<string, string>>(rawHeaders);
        }
        catch (JsonException exception)
        {
            throw new UnreadableTransportMessage($"SQS message attribute '{HeadersAttribute}' is not a JSON object of strings.", exception);
        }

        if (headers is null)
        {
            throw new UnreadableTransportMessage($"SQS message attribute '{HeadersAttribute}' is not a JSON object of strings.");
        }

        string? encoding = attributes.TryGetValue(BodyEncodingAttribute, out MessageAttributeValue? encodingAttribute)
            ? encodingAttribute.StringValue
            : null;
        byte[] bytes;
        if (encoding is null)
        {
            bytes = Encoding.UTF8.GetBytes(body);
        }
        else if (encoding == Base64)
        {
            try
            {
                bytes = Convert.FromBase64String(body);
            }
            catch (FormatException exception)
            {
                throw new UnreadableTransportMessage("SQS message body is not valid base64.", exception);
            }
        }
        else
        {
            throw new UnreadableTransportMessage($"SQS body encoding '{encoding}' is not supported.");
        }

        return new TransportMessage(headers, bytes);
    }

    private static MessageAttributeValue StringAttribute(string value) =>
        new() { DataType = "String", StringValue = value };

    private static string? TryText(ReadOnlySpan<byte> bytes)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        foreach (char c in text)
        {
            bool allowed = c is '\t' or '\n' or '\r' || (c >= ' ' && c is not ('\uFFFE' or '\uFFFF'));
            if (!allowed)
            {
                return null;
            }
        }

        return text;
    }
}
