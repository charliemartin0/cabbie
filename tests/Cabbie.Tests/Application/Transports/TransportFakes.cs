using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;

namespace Cabbie.Tests.Application.Transports;

/// <summary>Sender that records what it was given.</summary>
internal sealed class RecordingSender : ISender
{
    public List<Envelope> Sent { get; } = [];

    public int Disposals { get; private set; }

    public ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        Sent.Add(envelope);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposals++;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Transport for one scheme that records every sender and listener it creates; nothing
/// is sent anywhere. Listeners are driven by hand with <see cref="RecordingListener.PushAsync"/>.
/// </summary>
internal sealed class RecordingTransport(string scheme) : ITransport
{
    public string Scheme => scheme;

    public Dictionary<Uri, RecordingSender> Senders { get; } = [];

    public List<RecordingListener> Listeners { get; } = [];

    public IEnumerable<Envelope> Sent => Senders.Values.SelectMany(sender => sender.Sent);

    public int SenderCreations { get; private set; }

    public ISender CreateSender(Uri address)
    {
        SenderCreations++;
        if (!Senders.TryGetValue(address, out RecordingSender? sender))
        {
            sender = new RecordingSender();
            Senders.Add(address, sender);
        }

        return sender;
    }

    public IListener CreateListener(Uri address, IReceiver receiver)
    {
        var listener = new RecordingListener(address, receiver);
        Listeners.Add(listener);
        return listener;
    }
}

internal sealed class RecordingListener(Uri address, IReceiver receiver) : IListener
{
    public Uri Address => address;

    public bool Started { get; private set; }

    public bool Stopped { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Started = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stopped = true;
        return Task.CompletedTask;
    }

    /// <summary>Deliver an envelope as the transport would, and return what the receiver decided.</summary>
    public Task<ReceiveOutcome> PushAsync(Envelope envelope) =>
        receiver.ReceiveAsync(envelope, CancellationToken.None);
}

/// <summary>
/// Receiver that records envelopes and returns a configured outcome, optionally held
/// at a gate so tests can observe what a listener does while an envelope is in flight.
/// </summary>
internal sealed class RecordingReceiver(ReceiveOutcome outcome = ReceiveOutcome.Ack) : IReceiver
{
    private readonly List<Envelope> _received = [];
    private readonly object _gate = new();

    public TaskCompletionSource? Gate { get; init; }

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<Envelope> Received
    {
        get
        {
            lock (_gate)
            {
                return [.. _received];
            }
        }
    }

    public async Task<ReceiveOutcome> ReceiveAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _received.Add(envelope);
        }

        Entered.TrySetResult();
        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

        return outcome;
    }
}
