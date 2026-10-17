namespace Cabbie.Application.Transports;

/// <summary>
/// A transport carries Envelopes between endpoints. It owns one URI scheme (<c>local</c>
/// today; <c>sqs</c>, <c>sns</c> and others in adapter projects) and creates the two halves
/// of an endpoint: an <see cref="ISender"/> for outbound envelopes and an
/// <see cref="IListener"/> that hands inbound envelopes to an <see cref="IReceiver"/>.
/// <see cref="TransportRouter"/> picks the transport by the scheme of the endpoint URI.
/// Application knows only these ports; concrete transports are registered in Hosting
/// (<c>services.AddSingleton&lt;ITransport, MyTransport&gt;()</c>).
/// </summary>
public interface ITransport
{
    /// <summary>
    /// URI scheme this transport handles, e.g. <c>local</c>, <c>sqs</c>. Compared
    /// case-insensitively; at most one transport may own a scheme.
    /// </summary>
    string Scheme { get; }

    /// <summary>
    /// Outbound: a sender for one endpoint. The router caches it per endpoint URI, so
    /// implementations may hold connections or clients in it.
    /// </summary>
    ISender CreateSender(Uri address);

    /// <summary>
    /// Inbound: a listener that delivers every envelope arriving at <paramref name="address"/>
    /// to <paramref name="receiver"/> and acts on the returned <see cref="ReceiveOutcome"/>.
    /// Nothing is received until <see cref="IListener.StartAsync"/>.
    /// </summary>
    IListener CreateListener(Uri address, IReceiver receiver);
}
