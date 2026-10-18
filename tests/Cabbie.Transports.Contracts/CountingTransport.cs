using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;

namespace Cabbie.Transports.Contracts;

/// <summary>Wraps a transport and counts the senders it creates and disposes.</summary>
internal sealed class CountingTransport(ITransport inner) : ITransport
{
    private int _created;
    private int _disposed;

    public int SendersCreated => Volatile.Read(ref _created);

    public int SendersDisposed => Volatile.Read(ref _disposed);

    public string Scheme => inner.Scheme;

    public ISender CreateSender(Uri address)
    {
        ISender sender = inner.CreateSender(address);
        Interlocked.Increment(ref _created);
        return new CountingSender(sender, this);
    }

    public IListener CreateListener(Uri address, IReceiver receiver) => inner.CreateListener(address, receiver);

    private sealed class CountingSender(ISender inner, CountingTransport owner) : ISender
    {
        public ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default) =>
            inner.SendAsync(envelope, cancellationToken);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref owner._disposed);
            return inner.DisposeAsync();
        }
    }
}
