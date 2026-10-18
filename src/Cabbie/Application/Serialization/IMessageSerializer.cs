namespace Cabbie.Application.Serialization;

/// <summary>
/// Port for message body ↔ bytes. Used only at the transport boundary: <c>EnvelopeCodec</c>
/// serializes the body when an envelope leaves the process and deserializes it when one
/// arrives. The caller resolves the wire message name to a CLR type via MessageTypeCatalog
/// before reaching the serializer; names this host does not know are rejected by the codec,
/// not here. Replace the default by registering your own implementation.
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// Media type of the bytes this serializer writes and reads, e.g. <c>application/json</c>.
    /// Travels with the message so a receiver can refuse a format it cannot read.
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// Serialize a message body to bytes. <paramref name="messageType"/> is the concrete CLR type of the body.
    /// </summary>
    byte[] Serialize(object message, Type messageType);

    /// <summary>
    /// Deserialize bytes to a body of the requested CLR type.
    /// </summary>
    object Deserialize(ReadOnlyMemory<byte> data, Type messageType);
}
