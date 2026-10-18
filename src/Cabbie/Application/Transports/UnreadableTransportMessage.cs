namespace Cabbie.Application.Transports;

/// <summary>
/// An inbound <see cref="TransportMessage"/> cannot be turned back into an Envelope: a
/// required header is missing or malformed, the content type is not the one this host's
/// serializer reads, the message type is not known to this host, or the body does not
/// deserialize. The message states which; a body failure keeps the serializer's exception
/// as <see cref="Exception.InnerException"/>. Redelivery cannot fix such a message, so a
/// listener must acknowledge it and move it to a dead-letter destination, never nack it.
/// </summary>
public sealed class UnreadableTransportMessage : InvalidOperationException
{
    public UnreadableTransportMessage(string reason)
        : base(reason)
    {
    }

    public UnreadableTransportMessage(string reason, Exception innerException)
        : base(reason, innerException)
    {
    }
}
