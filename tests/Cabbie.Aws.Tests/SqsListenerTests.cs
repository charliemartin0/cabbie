using Amazon.SQS;
using Amazon.SQS.Model;
using Cabbie.Application.Serialization;
using Cabbie.Application.Transports;
using Cabbie.Domain.Messaging;
using Cabbie.Infrastructure.Serialization;
using Cabbie.Transports.Contracts;

namespace Cabbie.Aws.Tests;

/// <summary>SQS-specific behaviour the shared contract cannot express.</summary>
public sealed class SqsListenerTests(LocalStackFixture stack) : IClassFixture<LocalStackFixture>
{
    private static readonly SqsTransportOptions FastOptions = new()
    {
        VisibilityTimeout = TimeSpan.FromSeconds(2),
        WaitTime = TimeSpan.FromSeconds(1),
        NackDelay = TimeSpan.FromSeconds(1),
        PollErrorDelay = TimeSpan.FromSeconds(1),
    };

    private static EnvelopeCodec NewCodec()
    {
        var types = new MessageTypeCatalog();
        types.Register(typeof(ContractMessage));
        return new EnvelopeCodec(new JsonMessageSerializer(), types);
    }

    [Fact]
    public async Task unreadable_message_lands_in_the_dead_letter_queue_with_its_body_headers_and_a_reason()
    {
        using AmazonSQSClient client = stack.CreateClient();
        SqsQueuePair pair = await SqsQueuePair.CreateAsync(client);
        try
        {
            IListener listener = new SqsTransport(client, NewCodec(), FastOptions).CreateListener(pair.Endpoint, new NeverReceiver());
            await listener.StartAsync(CancellationToken.None);
            try
            {
                await client.SendMessageAsync(new SendMessageRequest
                {
                    QueueUrl = pair.QueueUrl,
                    MessageBody = "not json",
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        [SqsWire.HeadersAttribute] = new() { DataType = "String", StringValue = "{\"cabbie-message-id\":\"x\"}" },
                    },
                });

                Message deadLetter = await ReceiveOneAsync(client, pair.DeadLetterUrl);

                Assert.Equal("not json", deadLetter.Body);
                Assert.Equal("{\"cabbie-message-id\":\"x\"}", deadLetter.MessageAttributes[SqsWire.HeadersAttribute].StringValue);
                Assert.False(string.IsNullOrWhiteSpace(deadLetter.MessageAttributes[SqsWire.DeadLetterReasonAttribute].StringValue));
            }
            finally
            {
                await listener.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            await pair.DeleteAsync(client);
        }
    }

    [Fact]
    public async Task source_message_survives_when_the_dead_letter_write_fails()
    {
        using AmazonSQSClient client = stack.CreateClient();
        SqsQueuePair pair = await SqsQueuePair.CreateAsync(client);
        try
        {
            IListener listener = new SqsTransport(client, NewCodec(), FastOptions).CreateListener(pair.Endpoint, new NeverReceiver());
            await listener.StartAsync(CancellationToken.None);
            try
            {
                await client.DeleteQueueAsync(pair.DeadLetterUrl);
                await client.SendMessageAsync(new SendMessageRequest
                {
                    QueueUrl = pair.QueueUrl,
                    MessageBody = "not json",
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        [SqsWire.HeadersAttribute] = new() { DataType = "String", StringValue = "{}" },
                    },
                });

                await Task.Delay(TimeSpan.FromSeconds(6));
            }
            finally
            {
                await listener.StopAsync(CancellationToken.None);
            }

            GetQueueAttributesResponse attributes = await client.GetQueueAttributesAsync(
                pair.QueueUrl,
                ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible"]);
            Assert.Equal(1, attributes.ApproximateNumberOfMessages + attributes.ApproximateNumberOfMessagesNotVisible);
        }
        finally
        {
            await pair.DeleteAsync(client);
        }
    }

    [Fact]
    public async Task starting_a_listener_on_a_missing_queue_fails()
    {
        using AmazonSQSClient client = stack.CreateClient();
        IListener listener = new SqsTransport(client, NewCodec(), FastOptions)
            .CreateListener(new Uri($"sqs://missing-{Guid.NewGuid():N}/"), new NeverReceiver());

        await Assert.ThrowsAsync<QueueDoesNotExistException>(() => listener.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task sending_after_the_sender_is_disposed_throws()
    {
        using AmazonSQSClient client = stack.CreateClient();
        SqsQueuePair pair = await SqsQueuePair.CreateAsync(client);
        try
        {
            ISender sender = new SqsTransport(client, NewCodec(), FastOptions).CreateSender(pair.Endpoint);
            await sender.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await sender.SendAsync(null!, CancellationToken.None));
        }
        finally
        {
            await pair.DeleteAsync(client);
        }
    }

    private static async Task<Message> ReceiveOneAsync(AmazonSQSClient client, string url)
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

        throw new TimeoutException("No message arrived in the dead-letter queue.");
    }

    private sealed class NeverReceiver : IReceiver
    {
        public Task<ReceiveOutcome> ReceiveAsync(Cabbie.Domain.Envelope.Envelope envelope, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An unreadable message must not reach the receiver.");
    }
}
