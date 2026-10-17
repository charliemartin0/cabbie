using Cabbie.Application.Serialization;

namespace Cabbie.Infrastructure.Serialization;

/// <summary>
/// System.Text.Json implementation of <see cref="ISerializer"/>. Default content
/// type is application/json; the caller is responsible for setting
/// Envelope.ContentType on the surrounding Envelope.
/// </summary>
public sealed class JsonSerializer : ISerializer
{
    private readonly System.Text.Json.JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public byte[] SerializeBody(object body, Type bodyType)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(bodyType);
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(body, bodyType, _options);
    }

    public object DeserializeBody(byte[] data, Type bodyType)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(bodyType);
        object? result = System.Text.Json.JsonSerializer.Deserialize(data, bodyType, _options);
        return result ?? throw new System.Text.Json.JsonException(
            $"Deserializer returned null for body type {bodyType.FullName}");
    }
}
