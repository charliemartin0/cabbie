using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cabbie.Infrastructure.LocalQueues;

/// <summary>
/// One in-process queue worker for a destination. Owns a channel and a single reader task.
/// v1 is unbounded; back-pressure is a future slice. A durable queue is this agent plus Persistence.
/// Each envelope goes to the <see cref="IReceiver"/> (Execution, including cascade re-enqueueing);
/// a <see cref="ReceiveOutcome.Defer"/> puts it back on the queue, Ack and Nack drop it (local has
/// no redelivery: the error policy already ran and the receiver logged a Nack).
/// Items passed by reference go straight to the receiver. Encoded items (a <c>LocalTransport</c>
/// with a codec) are decoded here, on the listener side. An unreadable one never reaches the
/// receiver: it is acknowledged (dropped from the queue), kept in <c>DeadLetters</c> (the local
/// dead-letter destination) and logged.
/// </summary>
public sealed class LocalQueueAgent
{
    private readonly Channel<Delivery> _channel = Channel.CreateUnbounded<Delivery>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly ConcurrentQueue<TransportMessage> _deadLetters = new();
    private readonly IReceiver _receiver;
    private readonly ILogger _logger;
    private volatile TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile bool _paused;
    private volatile Task? _worker;
    private int _started;

    public LocalQueueAgent(string name, IReceiver receiver, ILogger<LocalQueueAgent>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(receiver);
        Name = name;
        _receiver = receiver;
        _logger = logger ?? NullLogger<LocalQueueAgent>.Instance;
    }

    internal IReceiver Receiver => _receiver;

    internal IReadOnlyList<TransportMessage> DeadLetters => [.. _deadLetters];

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
        _channel.Writer.TryWrite(new Delivery(envelope, null, null));
    }

    internal void Enqueue(TransportMessage message, EnvelopeCodec codec)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(codec);
        Start();
        _channel.Writer.TryWrite(new Delivery(null, message, codec));
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
            while (_channel.Reader.TryRead(out Delivery delivery))
            {
                await WaitWhilePausedAsync();
                try
                {
                    if (!TryOpen(delivery, out Envelope? envelope))
                    {
                        continue;
                    }

                    ReceiveOutcome outcome = await _receiver.ReceiveAsync(envelope, CancellationToken.None);
                    if (outcome == ReceiveOutcome.Defer)
                    {
                        if (!_channel.Writer.TryWrite(delivery))
                        {
                            LocalQueueLog.DeferredEnvelopeDropped(_logger, Name, envelope.Id.Value);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Trace.TraceError($"Local queue '{Name}' failed handling an envelope: {exception}");
                }
            }
        }
    }

    private bool TryOpen(Delivery delivery, [NotNullWhen(true)] out Envelope? envelope)
    {
        if (delivery.Envelope is { } byReference)
        {
            envelope = byReference;
            return true;
        }

        if (delivery is { Message: { } message, Codec: { } codec })
        {
            try
            {
                envelope = codec.Decode(message);
                return true;
            }
            catch (UnreadableTransportMessage exception)
            {
                _deadLetters.Enqueue(message);
                LocalQueueLog.UnreadableMessageDeadLettered(_logger, exception, Name);
                envelope = null;
                return false;
            }
        }

        throw new UnreachableException();
    }

    private readonly record struct Delivery(Envelope? Envelope, TransportMessage? Message, EnvelopeCodec? Codec);
}