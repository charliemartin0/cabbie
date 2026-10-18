using System.Reflection;
using Amazon.SQS;
using Amazon.SQS.Model;
using Cabbie.Application.Serialization;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Infrastructure.Serialization;
using Cabbie.Transports.Contracts;
using Microsoft.Extensions.Time.Testing;
using DomainMessage = Cabbie.Domain.Messaging.ValueObjects.Message;
using SqsMessage = Amazon.SQS.Model.Message;

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

                SqsMessage deadLetter = await ReceiveOneAsync(client, pair.DeadLetterUrl);

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

    private static async Task<SqsMessage> ReceiveOneAsync(AmazonSQSClient client, string url)
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

    private sealed class CollectingReceiver : IReceiver
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task<ReceiveOutcome> ReceiveAsync(Cabbie.Domain.Envelope.Envelope envelope, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(ReceiveOutcome.Ack);
        }
    }

    /// <summary>Throws a non-AWS exception for the first receive calls, then delegates to the real client.</summary>
    public class FaultingSqs : DispatchProxy
    {
        private IAmazonSQS _inner = null!;
        private int _faultsLeft;

        public static IAmazonSQS Wrap(IAmazonSQS inner, int faults)
        {
            IAmazonSQS proxy = Create<IAmazonSQS, FaultingSqs>();
            var faulting = (FaultingSqs)(object)proxy;
            faulting._inner = inner;
            faulting._faultsLeft = faults;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == nameof(IAmazonSQS.ReceiveMessageAsync) && Interlocked.Decrement(ref _faultsLeft) >= 0)
            {
                throw new InvalidOperationException("simulated SDK failure");
            }

            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw exception.InnerException;
            }
        }
    }

    [Fact]
    public async Task listener_keeps_polling_after_unexpected_receive_errors_and_delivers_messages()
    {
        using AmazonSQSClient client = stack.CreateClient();
        SqsQueuePair pair = await SqsQueuePair.CreateAsync(client);
        try
        {
            var time = new FakeTimeProvider();
            var receiver = new CollectingReceiver();
            EnvelopeCodec codec = NewCodec();
            IListener listener = new SqsTransport(FaultingSqs.Wrap(client, faults: 2), codec, FastOptions, timeProvider: time)
                .CreateListener(pair.Endpoint, receiver);
            await listener.StartAsync(CancellationToken.None);
            try
            {
                ISender sender = new SqsTransport(client, codec, FastOptions).CreateSender(pair.Endpoint);
                var envelope = new Cabbie.Domain.Envelope.Envelope(
                    new EnvelopeId(Guid.NewGuid()),
                    new DomainMessage(new ContractMessage(1, "message 1")),
                    MessageTypeNaming.For(typeof(ContractMessage)),
                    new Destination(pair.Endpoint),
                    new CorrelationId(Guid.NewGuid()),
                    new ConversationId(Guid.NewGuid()),
                    new SagaId(""),
                    new SentAt(DateTimeOffset.UtcNow),
                    new DeliverBy(null),
                    new Headers(),
                    new ContentType(""),
                    new Attempts(1),
                    new EnvelopeData(),
                    null);
                await sender.SendAsync(envelope, CancellationToken.None);

                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                while (receiver.Count == 0 && DateTime.UtcNow < deadline)
                {
                    time.Advance(TimeSpan.FromSeconds(5));
                    await Task.Delay(20);
                }

                Assert.Equal(1, receiver.Count);
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
    public void max_poll_error_delay_below_the_poll_error_delay_is_rejected()
    {
        using var client = new AmazonSQSClient(new Amazon.Runtime.AnonymousAWSCredentials(), new AmazonSQSConfig { ServiceURL = "http://localhost:1" });
        var options = new SqsTransportOptions { PollErrorDelay = TimeSpan.FromSeconds(10), MaxPollErrorDelay = TimeSpan.FromSeconds(1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new SqsTransport(client, NewCodec(), options));
    }
}
