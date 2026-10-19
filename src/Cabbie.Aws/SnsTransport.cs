using Amazon.SimpleNotificationService;
using Cabbie.Application.Transports;

namespace Cabbie.Aws;

/// <summary>
/// The <c>sns://</c> transport, publish only. An address names an existing topic
/// (<c>sns://orders/</c>); neither the topic nor its subscriptions are created here. Subscribers are
/// SQS queues, listened to with <see cref="SqsTransport"/>; each subscription must set
/// <c>RawMessageDelivery=true</c> (otherwise SNS wraps the body and the listener dead-letters it) and
/// the queue policy must allow <c>sns:SendMessage</c> from the topic. Headers are packed exactly as
/// <see cref="SqsTransport"/> packs them. The caller owns the <see cref="IAmazonSimpleNotificationService"/>
/// client: the transport never disposes it.
/// </summary>
public sealed class SnsTransport : ITransport
{
    private readonly IAmazonSimpleNotificationService _client;
    private readonly EnvelopeCodec _codec;

    public SnsTransport(IAmazonSimpleNotificationService client, EnvelopeCodec codec)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(codec);
        _client = client;
        _codec = codec;
    }

    public string Scheme => SnsEndpoint.Scheme;

    public ISender CreateSender(Uri address) => new SnsSender(_client, _codec, SnsEndpoint.Parse(address));

    public IListener CreateListener(Uri address, IReceiver receiver) =>
        throw new NotSupportedException("sns:// is publish-only; listen on a subscribed sqs:// queue.");
}
