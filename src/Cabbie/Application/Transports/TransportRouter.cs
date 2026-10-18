using System.Collections.Concurrent;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Transports;

/// <summary>
/// Registry of transports keyed by URI scheme, and the <see cref="ISender"/> every
/// outbound envelope goes through: the destination's scheme picks the transport and the
/// sender for that endpoint is created exactly once and cached. A scheme nobody owns fails
/// with <see cref="UnknownTransportScheme"/> instead of silently running in-process.
/// Disposing the router disposes the cached senders; a later send creates fresh ones.
/// </summary>
public sealed class TransportRouter : ISender
{
    private readonly Dictionary<string, ITransport> _transports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Uri, Lazy<ISender>> _senders = new();

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
        Uri destination = envelope.Destination.Value;
        Lazy<ISender> entry = _senders.GetOrAdd(destination, uri => new Lazy<ISender>(() => For(uri).CreateSender(uri)));
        ISender sender;
        try
        {
            sender = entry.Value;
        }
        catch
        {
            // A failed factory must not be cached: the next send tries again.
            _senders.TryRemove(new KeyValuePair<Uri, Lazy<ISender>>(destination, entry));
            throw;
        }

        return sender.SendAsync(envelope, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Uri key in _senders.Keys.ToList())
        {
            if (_senders.TryRemove(key, out Lazy<ISender>? entry) && entry.IsValueCreated)
            {
                await entry.Value.DisposeAsync();
            }
        }
    }
}
