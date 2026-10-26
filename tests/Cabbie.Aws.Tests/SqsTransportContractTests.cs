using Amazon.SQS;
using Amazon.SQS.Model;
using Cabbie.Application.Transports;
using Cabbie.Transports.Contracts;

namespace Cabbie.Aws.Tests;

/// <summary>
/// <c>sqs://</c> against LocalStack. SQS redelivers a nacked envelope and lets listeners on one
/// queue compete. The quiet window sits above the visibility timeout, so "never redelivered"
/// checks span at least one redelivery.
/// </summary>
public sealed class SqsTransportContractTests(LocalStackFixture stack) : TransportContract, IClassFixture<LocalStackFixture>
{
    private readonly Dictionary<Uri, SqsQueuePair> _queues = [];
    private readonly Dictionary<Uri, List<TransportMessage>> _deadLetters = [];
    private AmazonSQSClient? _client;

    protected override TransportCapabilities Capabilities { get; } =
        new(NackBehaviour.Redeliver, SharedAddressBehaviour.CompetingConsumers);

    protected override TransportContractTimings Timings { get; } =
        new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

    private AmazonSQSClient Client => _client ?? throw new InvalidOperationException("InitializeAsync has not run.");

    protected override Task<ITransport> CreateTransportAsync(EnvelopeCodec codec)
    {
        _client = stack.CreateClient();
        var options = new SqsTransportOptions
        {
            VisibilityTimeout = TimeSpan.FromSeconds(2),
            WaitTime = TimeSpan.FromSeconds(1),
            NackDelay = TimeSpan.FromSeconds(1),
            PollErrorDelay = TimeSpan.FromSeconds(1),
        };
        return Task.FromResult<ITransport>(new SqsTransport(_client, codec, options));
    }

    protected override async Task<Uri> CreateEndpointAsync()
    {
        SqsQueuePair pair = await SqsQueuePair.CreateAsync(Client);
        _queues[pair.Endpoint] = pair;
        _deadLetters[pair.Endpoint] = [];
        return pair.Endpoint;
    }

    protected override async Task SendRawAsync(Uri endpoint, TransportMessage message)
    {
        (string body, Dictionary<string, MessageAttributeValue> attributes) = SqsWire.Pack(message);
        await Client.SendMessageAsync(
            new SendMessageRequest { QueueUrl = _queues[endpoint].QueueUrl, MessageBody = body, MessageAttributes = attributes });
    }

    protected override async Task<IReadOnlyList<TransportMessage>> ReadDeadLettersAsync(Uri endpoint)
    {
        List<TransportMessage> seen = _deadLetters[endpoint];
        string url = _queues[endpoint].DeadLetterUrl;
        while (true)
        {
            ReceiveMessageResponse response = await Client.ReceiveMessageAsync(
                new ReceiveMessageRequest
                {
                    QueueUrl = url,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 1,
                    MessageAttributeNames = ["All"],
                });
            if (response.Messages is null or { Count: 0 })
            {
                return [.. seen];
            }

            foreach (Message message in response.Messages)
            {
                seen.Add(SqsWire.Unpack(message.Body, message.MessageAttributes));
                await Client.DeleteMessageAsync(url, message.ReceiptHandle);
            }
        }
    }

    protected override async Task DisposeTransportAsync()
    {
        if (_client is null)
        {
            return;
        }

        foreach (SqsQueuePair pair in _queues.Values)
        {
            await pair.DeleteAsync(_client);
        }

        _client.Dispose();
    }
}
