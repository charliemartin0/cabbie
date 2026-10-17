using System.Text.Json;
using System.Text.Json.Serialization;
using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Postgresql;

/// <summary>
/// Serializes <see cref="EnvelopeData"/> as a single base64 string. <see cref="EnvelopeData.Value"/>
/// is a <see cref="ReadOnlyMemory{T}"/> of bytes that System.Text.Json does not natively handle;
/// this converter keeps the JSON shape flat (<c>"Data": "&lt;base64&gt;"</c>) instead of an
/// awkward nested object.
/// </summary>
internal sealed class EnvelopeDataJsonConverter : JsonConverter<EnvelopeData>
{
    public override EnvelopeData? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new EnvelopeData();
        }

        byte[] bytes = reader.GetBytesFromBase64();
        return new EnvelopeData(bytes);
    }

    public override void Write(Utf8JsonWriter writer, EnvelopeData value, JsonSerializerOptions options)
    {
        if (value.Value.IsEmpty)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(Convert.ToBase64String(value.Value.Span));
        }
    }
}
