using System.Diagnostics;
using System.Threading.Channels;
using Cabbie.Application.Bus;
using Cabbie.Domain.Envelope;

namespace Cabbie.Infrastructure.LocalQueues;

/// <summary>
/// One in-process queue worker for a destination. Owns a channel and a single reader task.
/// v1 is unbounded; back-pressure is a future slice. A durable queue is this agent plus Persistence.
/// Dispatch (including cascade re-enqueueing) goes through the shared <see cref="MessageDelivery"/>.
/// </summary>
public sealed class LocalQueueAgent
{
    private readonly Channel<Envelope> _channel = Channel.CreateUnbounded<Envelope>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly DispatchHandler _dispatch;
    private volatile TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile bool _paused;
    private volatile Task? _worker;
    private int _started;

    public LocalQueueAgent(string name, MessageDelivery delivery)
        : this(name, (envelope, cancellationToken) =>
            delivery.Dispatch(envelope, scheduled: true, cancellationToken))
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(delivery);
    }

    /// <summary>
    /// Dispatch hook for the host: queue messages must route saga handlers through the
    /// saga orchestration (load/save), not the plain executor path that would invoke
    /// them on a fresh instance and drop the state.
    /// </summary>
    internal LocalQueueAgent(string name, DispatchHandler dispatch)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(dispatch);
        Name = name;
        _dispatch = dispatch;
    }

    public string Name { get; }

    public int QueueLength => _channel.Reader.Count;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _worker = Task.Run(RunAsync);
    }

    public void Enqueue(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        Start();
        _channel.Writer.TryWrite(envelope);
    }

    public void Pause()
    {
        _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _paused = true;
    }

    public void Resume()
    {
        _paused = false;
        _gate.TrySetResult();
    }

    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        Task? worker = _worker;
        if (worker is not null)
        {
            await worker.WaitAsync(cancellationToken);
        }
    }

    private Task WaitWhilePausedAsync() => _paused ? _gate.Task : Task.CompletedTask;

    private async Task RunAsync()
    {
        while (await _channel.Reader.WaitToReadAsync())
        {
            while (_channel.Reader.TryRead(out Envelope? envelope))
            {
                await WaitWhilePausedAsync();
                try
                {
                    await _dispatch(envelope, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    Trace.TraceError($"Local queue '{Name}' failed handling an envelope: {exception}");
                }
            }
        }
    }
}