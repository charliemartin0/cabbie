using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Infrastructure.Serialization;
using Cabbie.Transports.Contracts;
using Envelope = Cabbie.Domain.Envelope.Envelope;
using Message = Cabbie.Domain.Messaging.ValueObjects.Message;
using SnsAttribute = Amazon.SimpleNotificationService.Model.MessageAttributeValue;
using SqsMessage = Amazon.SQS.Model.Message;

namespace Cabbie.Aws.Tests;

/// <summary><c>sns://</c> fan-out to raw-delivery SQS subscriptions against LocalStack.</summary>
public sealed class SnsTransportTests(LocalStackFixture stack) : IClassFixture<LocalStackFixture>
{
    private static readonly SqsTransportOptions FastOptions = new()
    {
        VisibilityTimeout = TimeSpan.FromSeconds(2),
        WaitTime = TimeSpan.FromSeconds(1),
        NackDelay = TimeSpan.FromSeconds(1),
        PollErrorDelay = TimeSpan.FromSeconds(1),
    };

    [Fact]
    public async Task publish_reaches_every_subscribed_queue_listener_with_headers_and_body_intact()
    {
        await using var scope = new Scope(stack);
        string topicArn = await scope.CreateTopicAsync();
        Recorder first = await scope.ListenOnSubscribedQueueAsync(topicArn);
        Recorder second = await scope.ListenOnSubscribedQueueAsync(topicArn);
        EnvelopeCodec codec = NewCodec();
        Envelope sent = NewEnvelope(
            new Uri($"sns://{TopicName(topicArn)}/"),
            new Headers(new Dictionary<string, string> { ["tenant"] = "acme", ["trace-id"] = "abc-123" }));

        await using ISender sender = new SnsTransport(scope.Sns, codec).CreateSender(sent.Destination.Value);
        await sender.SendAsync(sent);
        await WaitUntilAsync(() => first.Received.Count == 1 && second.Received.Count == 1);

        foreach (Recorder recorder in new[] { first, second })
        {
            Envelope received = Assert.Single(recorder.Received);
            Assert.Equal(sent.Id, received.Id);
            Assert.Equal(sent.Headers, received.Headers);
            Assert.Equal(new ContractMessage(7, "message 7"), received.Message.Value);
        }
    }

    [Fact]
    public async Task a_binary_body_survives_fan_out_through_the_base64_attribute()
    {
        await using var scope = new Scope(stack);
        string topicArn = await scope.CreateTopicAsync();
        SqsQueuePair pair = await scope.SubscribeQueueAsync(topicArn);
        byte[] body = [0xFF, 0x00, 0xFE, 0x01];
        var message = new TransportMessage(new Dictionary<string, string> { ["tenant"] = "acme" }, body);
        (string text, Dictionary<string, Amazon.SQS.Model.MessageAttributeValue> packed) = SqsWire.Pack(message);

        await scope.Sns.PublishAsync(new PublishRequest
        {
            TopicArn = topicArn,
            Message = text,
            MessageAttributes = packed.ToDictionary(
                pair => pair.Key,
                pair => new SnsAttribute { DataType = pair.Value.DataType, StringValue = pair.Value.StringValue }),
        });
        SqsMessage delivered = await ReceiveOneAsync(scope.Sqs, pair.QueueUrl);

        TransportMessage unpacked = SqsWire.Unpack(delivered.Body, delivered.MessageAttributes);
        Assert.Equal(body, unpacked.Body.ToArray());
        Assert.Equal("acme", unpacked.Headers["tenant"]);
    }

    [Fact]
    public async Task publish_with_no_subscription_delivers_nothing_and_does_not_throw()
    {
        await using var scope = new Scope(stack);
        string topicArn = await scope.CreateTopicAsync();
        Recorder unsubscribed = await scope.ListenOnUnsubscribedQueueAsync();
        Envelope sent = NewEnvelope(new Uri($"sns://{TopicName(topicArn)}/"), new Headers());

        await using ISender sender = new SnsTransport(scope.Sns, NewCodec()).CreateSender(sent.Destination.Value);
        await sender.SendAsync(sent);

        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Empty(unsubscribed.Received);
    }

    [Fact]
    public async Task sending_to_a_missing_topic_throws_not_found()
    {
        await using var scope = new Scope(stack);
        Envelope sent = NewEnvelope(new Uri($"sns://missing-{Guid.NewGuid():N}/"), new Headers());
        await using ISender sender = new SnsTransport(scope.Sns, NewCodec()).CreateSender(sent.Destination.Value);

        await Assert.ThrowsAsync<NotFoundException>(async () => await sender.SendAsync(sent));
    }

    [Fact]
    public async Task a_failed_topic_lookup_is_retried_by_the_next_send()
    {
        await using var scope = new Scope(stack);
        string name = $"late-{Guid.NewGuid():N}";
        Envelope sent = NewEnvelope(new Uri($"sns://{name}/"), new Headers());
        await using ISender sender = new SnsTransport(scope.Sns, NewCodec()).CreateSender(sent.Destination.Value);
        await Assert.ThrowsAsync<NotFoundException>(async () => await sender.SendAsync(sent));

        string topicArn = await scope.CreateTopicAsync(name);
        Recorder recorder = await scope.ListenOnSubscribedQueueAsync(topicArn);
        await sender.SendAsync(sent);

        await WaitUntilAsync(() => recorder.Received.Count == 1);
        Assert.Equal(sent.Id, Assert.Single(recorder.Received).Id);
    }

    [Fact]
    public async Task creating_a_listener_on_sns_is_not_supported()
    {
        await using var scope = new Scope(stack);
        var transport = new SnsTransport(scope.Sns, NewCodec());

        Assert.Throws<NotSupportedException>(() => transport.CreateListener(new Uri("sns://orders/"), new Recorder()));
    }

    [Fact]
    public async Task sending_after_the_sender_is_disposed_throws()
    {
        await using var scope = new Scope(stack);
        ISender sender = new SnsTransport(scope.Sns, NewCodec()).CreateSender(new Uri("sns://orders/"));
        await sender.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await sender.SendAsync(null!));
    }

    private static string TopicName(string topicArn) => topicArn[(topicArn.LastIndexOf(':') + 1)..];

    private static EnvelopeCodec NewCodec()
    {
        var types = new MessageTypeCatalog();
        types.Register(typeof(ContractMessage));
        return new EnvelopeCodec(new JsonMessageSerializer(), types);
    }

    private static Envelope NewEnvelope(Uri destination, Headers headers) =>
        new(
            new EnvelopeId(Guid.NewGuid()),
            new Message(new ContractMessage(7, "message 7")),
            MessageTypeNaming.For(typeof(ContractMessage)),
            new Destination(destination),
            new CorrelationId(Guid.NewGuid()),
            new ConversationId(Guid.NewGuid()),
            new SagaId(""),
            new SentAt(TimeProvider.System.GetUtcNow()),
            new DeliverBy(null),
            headers,
            new ContentType(""),
            new Attempts(1),
            new EnvelopeData(),
            null);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for the envelopes to be delivered.");
            }

            await Task.Delay(50);
        }
    }

    private static async Task<SqsMessage> ReceiveOneAsync(IAmazonSQS client, string url)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            ReceiveMessageResponse response = await client.ReceiveMessageAsync(
                new ReceiveMessageRequest { QueueUrl = url, WaitTimeSeconds = 1, MessageAttributeNames = ["All"] });
            if (response.Messages is { Count: > 0 } messages)
            {
                return messages[0];
            }
        }

        throw new TimeoutException("No message arrived on the subscribed queue.");
    }

    private sealed class Recorder : IReceiver
    {
        private readonly Lock _lock = new();
        private readonly List<Envelope> _received = [];

        public IReadOnlyList<Envelope> Received
        {
            get
            {
                lock (_lock)
                {
                    return [.. _received];
                }
            }
        }

        public Task<ReceiveOutcome> ReceiveAsync(Envelope envelope, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _received.Add(envelope);
            }

            return Task.FromResult(ReceiveOutcome.Ack);
        }
    }

    /// <summary>Owns the clients, topics, queues and listeners one test creates and removes them all.</summary>
    private sealed class Scope(LocalStackFixture stack) : IAsyncDisposable
    {
        private readonly List<string> _topicArns = [];
        private readonly List<SqsQueuePair> _queues = [];
        private readonly List<IListener> _listeners = [];
        private readonly EnvelopeCodec _codec = NewCodec();

        public AmazonSimpleNotificationServiceClient Sns { get; } = stack.CreateSnsClient();

        public AmazonSQSClient Sqs { get; } = stack.CreateClient();

        public async Task<string> CreateTopicAsync(string? name = null)
        {
            string arn = (await Sns.CreateTopicAsync(name ?? $"contract-{Guid.NewGuid():N}")).TopicArn;
            _topicArns.Add(arn);
            return arn;
        }

        public async Task<SqsQueuePair> SubscribeQueueAsync(string topicArn)
        {
            SqsQueuePair pair = await CreateQueueAsync();
            string queueArn = (await Sqs.GetQueueAttributesAsync(pair.QueueUrl, ["QueueArn"])).QueueARN;
            await Sns.SubscribeAsync(new SubscribeRequest
            {
                TopicArn = topicArn,
                Protocol = "sqs",
                Endpoint = queueArn,
                Attributes = new Dictionary<string, string> { ["RawMessageDelivery"] = "true" },
            });
            return pair;
        }

        public async Task<Recorder> ListenOnSubscribedQueueAsync(string topicArn) =>
            await ListenAsync(await SubscribeQueueAsync(topicArn));

        public async Task<Recorder> ListenOnUnsubscribedQueueAsync() => await ListenAsync(await CreateQueueAsync());

        public async ValueTask DisposeAsync()
        {
            foreach (IListener listener in _listeners)
            {
                await listener.StopAsync(CancellationToken.None);
            }

            foreach (string arn in _topicArns)
            {
                await Sns.DeleteTopicAsync(arn);
            }

            foreach (SqsQueuePair pair in _queues)
            {
                await pair.DeleteAsync(Sqs);
            }

            Sns.Dispose();
            Sqs.Dispose();
        }

        private async Task<SqsQueuePair> CreateQueueAsync()
        {
            SqsQueuePair pair = await SqsQueuePair.CreateAsync(Sqs);
            _queues.Add(pair);
            return pair;
        }

        private async Task<Recorder> ListenAsync(SqsQueuePair pair)
        {
            var recorder = new Recorder();
            IListener listener = new SqsTransport(Sqs, _codec, FastOptions).CreateListener(pair.Endpoint, recorder);
            await listener.StartAsync(CancellationToken.None);
            _listeners.Add(listener);
            return recorder;
        }
    }
}
