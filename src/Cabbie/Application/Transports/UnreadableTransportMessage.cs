namespace Cabbie.Application.Transports;

/// <summary>
/// An inbound <see cref="TransportMessage"/> cannot be turned back into an Envelope: a
/// required header is missing or malformed, the content type is not the one this host's
/// serializer reads, or the message type is not known to this host. The message states which.
/// </summary>
public sealed class UnreadableTransportMessage : InvalidOperationException
{
    public UnreadableTransportMessage(string reason)
        : base(reason)
    {
    }
}
