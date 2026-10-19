using Amazon.SQS;
using Amazon.SQS.Model;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;

namespace Cabbie.Aws;

/// <summary>Sends envelopes to one queue. Does not own the client; disposing only stops further sends.</summary>
internal sealed class SqsSender(IAmazonSQS client, EnvelopeCodec codec, SqsEndpoint endpoint) : ISender
{
    private readonly object _urlLock = new();
    private Task<string>? _queueUrl;
    private volatile bool _disposed;

    public async ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(envelope);
        (string body, Dictionary<string, MessageAttributeValue> attributes) = SqsWire.Pack(codec.Encode(envelope));
        string queueUrl = await QueueUrlAsync(cancellationToken);
        await client.SendMessageAsync(
            new SendMessageRequest { QueueUrl = queueUrl, MessageBody = body, MessageAttributes = attributes },
            cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    // Only a successful resolution stays cached: a missing queue is retried by the next send.
    private async Task<string> QueueUrlAsync(CancellationToken cancellationToken)
    {
        Task<string> pending;
        lock (_urlLock)
        {
            _queueUrl ??= SqsQueues.ResolveAsync(client, endpoint.QueueName);
            pending = _queueUrl;
        }

        try
        {
            return await pending.WaitAsync(cancellationToken);
        }
        catch (Exception) when (pending.IsFaulted || pending.IsCanceled)
        {
            lock (_urlLock)
            {
                if (ReferenceEquals(_queueUrl, pending))
                {
                    _queueUrl = null;
                }
            }

            throw;
        }
    }
}
