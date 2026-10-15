namespace Cabbie.Application.Serialization;

/// <summary>
/// Port for Envelope body ↔ bytes. Implementations encode the message body using
/// Domain/Messaging type names where polymorphism is required. The caller resolves
/// MessageType to CLR Type via MessageTypeCatalog before reaching the serializer;
/// unknown CLR types are handed off to IMissingHandler at the Transport layer, not
/// crashed here.
/// </summary>
public interface ISerializer
{
    /// <summary>
    /// Serialize a message body to bytes. bodyType guides System.Text.Json's
    /// polymorphism metadata; pass the concrete CLR type of the body.
    /// </summary>
    byte[] SerializeBody(object body, Type bodyType);

    /// <summary>
    /// Deserialize bytes to a body of the requested CLR type.
    /// </summary>
    object DeserializeBody(byte[] data, Type bodyType);
}
