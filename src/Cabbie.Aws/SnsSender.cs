using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using SqsAttribute = Amazon.SQS.Model.MessageAttributeValue;

namespace Cabbie.Aws;

/// <summary>Publishes envelopes to one topic. Does not own the client; disposing only stops further sends.</summary>
internal sealed class SnsSender(IAmazonSimpleNotificationService client, EnvelopeCodec codec, SnsEndpoint endpoint) : ISender
{
    private readonly object _arnLock = new();
    private Task<string>? _topicArn;
    private volatile bool _disposed;

    public async ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(envelope);
        (string body, Dictionary<string, SqsAttribute> packed) = SqsWire.Pack(codec.Encode(envelope));
        var attributes = new Dictionary<string, MessageAttributeValue>(packed.Count, StringComparer.Ordinal);
        foreach ((string name, SqsAttribute value) in packed)
        {
            attributes[name] = new MessageAttributeValue { DataType = value.DataType, StringValue = value.StringValue };
        }

        string topicArn = await TopicArnAsync(cancellationToken);
        await client.PublishAsync(
            new PublishRequest { TopicArn = topicArn, Message = body, MessageAttributes = attributes },
            cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    // Only a successful resolution stays cached: a missing topic is retried by the next send.
    private async Task<string> TopicArnAsync(CancellationToken cancellationToken)
    {
        Task<string> pending;
        lock (_arnLock)
        {
            _topicArn ??= SnsTopics.ResolveArnAsync(client, endpoint.TopicName, CancellationToken.None);
            pending = _topicArn;
        }

        try
        {
            return await pending.WaitAsync(cancellationToken);
        }
        catch (Exception) when (pending.IsFaulted || pending.IsCanceled)
        {
            lock (_arnLock)
            {
                if (ReferenceEquals(_topicArn, pending))
                {
                    _topicArn = null;
                }
            }

            throw;
        }
    }
}
