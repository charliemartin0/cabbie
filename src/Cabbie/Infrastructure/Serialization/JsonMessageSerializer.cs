using Cabbie.Application.Serialization;

namespace Cabbie.Infrastructure.Serialization;

/// <summary>
/// System.Text.Json implementation of <see cref="IMessageSerializer"/>. Content type is
/// application/json.
/// </summary>
public sealed class JsonMessageSerializer : IMessageSerializer
{
    private readonly System.Text.Json.JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public string ContentType => "application/json";

    public byte[] Serialize(object message, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageType);
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(message, messageType, _options);
    }

    public object Deserialize(ReadOnlyMemory<byte> data, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        object? result = System.Text.Json.JsonSerializer.Deserialize(data.Span, messageType, _options);
        return result ?? throw new System.Text.Json.JsonException(
            $"Deserializer returned null for body type {messageType.FullName}");
    }
}
