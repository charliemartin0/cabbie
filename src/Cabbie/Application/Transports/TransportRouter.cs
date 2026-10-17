using System.Collections.Concurrent;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Transports;

/// <summary>
/// Registry of transports keyed by URI scheme, and the <see cref="ISender"/> every
/// outbound envelope goes through: the destination's scheme picks the transport and the
/// sender for that endpoint is created once and cached. A scheme nobody owns fails with
/// <see cref="UnknownTransportScheme"/> instead of silently running in-process.
/// </summary>
public sealed class TransportRouter : ISender
{
    private readonly Dictionary<string, ITransport> _transports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Uri, ISender> _senders = new();

    public TransportRouter(IEnumerable<ITransport> transports)
    {
        ArgumentNullException.ThrowIfNull(transports);
        foreach (ITransport transport in transports)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(transport.Scheme);
            if (!_transports.TryAdd(transport.Scheme, transport))
            {
                throw new DuplicateTransportScheme(
                    transport.Scheme,
                    [_transports[transport.Scheme].GetType(), transport.GetType()]);
            }
        }
    }

    public IReadOnlyCollection<string> Schemes => _transports.Keys;

    /// <summary>The transport that owns the endpoint's scheme.</summary>
    public ITransport For(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Endpoint URI must be absolute, e.g. local://orders.", nameof(endpoint));
        }

        return _transports.TryGetValue(endpoint.Scheme, out ITransport? transport)
            ? transport
            : throw new UnknownTransportScheme(endpoint, Schemes);
    }

    public ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return _senders
            .GetOrAdd(envelope.Destination.Value, uri => For(uri).CreateSender(uri))
            .SendAsync(envelope, cancellationToken);
    }
}
