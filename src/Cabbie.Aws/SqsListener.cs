using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Microsoft.Extensions.Logging;

namespace Cabbie.Aws;

/// <summary>
/// Long-polls one queue and hands each message to the receiver. Outcomes: Ack deletes the message,
/// Nack hides it for <see cref="SqsTransportOptions.NackDelay"/>, Defer makes it visible at once. A message
/// that cannot be decoded is copied to the dead-letter queue and only then deleted from the source.
/// </summary>
internal sealed class SqsListener(
    Uri address,
    SqsEndpoint endpoint,
    IAmazonSQS client,
    EnvelopeCodec codec,
    IReceiver receiver,
    SqsTransportOptions options,
    ILogger logger,
    TimeProvider timeProvider) : IListener, IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _pollCts = new();
    private readonly CancellationTokenSource _processCts = new();
    private bool _started;
    private bool _stopRequested;
    private bool _closed;
    private Task? _loop;
    private string _queueUrl = "";
    private string _deadLetterUrl = "";

    public Uri Address => address;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_started || _stopRequested)
            {
                throw new InvalidOperationException($"The listener for {address} was already started or stopped.");
            }

            _started = true;
        }

        try
        {
            _queueUrl = await SqsQueues.ResolveAsync(client, endpoint.QueueName).WaitAsync(cancellationToken);
            _deadLetterUrl = await SqsQueues.ResolveAsync(client, endpoint.DeadLetterQueueName).WaitAsync(cancellationToken);
        }
        catch
        {
            lock (_gate)
            {
                _stopRequested = true;
                Close();
            }

            throw;
        }

        lock (_gate)
        {
            _loop = Task.Run(RunAsync, CancellationToken.None);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? loop;
        lock (_gate)
        {
            _stopRequested = true;
            loop = _loop;
            if (loop is null)
            {
                Close();
                return;
            }

            Cancel(_pollCts);
        }

        try
        {
            await loop.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                Cancel(_processCts);
            }

            throw;
        }
    }

    /// <summary>Cancels anything still running and releases the tokens; <see cref="StopAsync"/> does the same once the loop ends.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _stopRequested = true;
            if (_loop is null)
            {
                Close();
                return;
            }

            Cancel(_pollCts);
            Cancel(_processCts);
        }
    }

    // Callers hold _gate.
    private static void Cancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already closed: the loop finished.
        }
    }

    // Callers hold _gate.
    private void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _pollCts.Dispose();
        _processCts.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            CancellationToken pollToken = _pollCts.Token;
            int consecutiveFailures = 0;
            while (!pollToken.IsCancellationRequested)
            {
                IReadOnlyList<Message> batch;
                try
                {
                    ReceiveMessageResponse response = await client.ReceiveMessageAsync(
                        new ReceiveMessageRequest
                        {
                            QueueUrl = _queueUrl,
                            MaxNumberOfMessages = options.MaxMessages,
                            WaitTimeSeconds = (int)options.WaitTime.TotalSeconds,
                            VisibilityTimeout = (int)options.VisibilityTimeout.TotalSeconds,
                            MessageAttributeNames = ["All"],
                        },
                        pollToken);
                    batch = response.Messages ?? [];
                }
                catch (OperationCanceledException) when (pollToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    consecutiveFailures++;
                    SqsLog.ReceiveFailed(logger, exception, endpoint.QueueName);
                    try
                    {
                        await Task.Delay(BackoffFor(consecutiveFailures), timeProvider, pollToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    continue;
                }

                bool[] results = await Task.WhenAll(batch.Select(ProcessGuardedAsync));
                if (!results.Contains(false))
                {
                    consecutiveFailures = 0;
                    continue;
                }

                consecutiveFailures++;
                try
                {
                    await Task.Delay(BackoffFor(consecutiveFailures), timeProvider, pollToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                Close();
            }
        }
    }

    // Doubles from PollErrorDelay per consecutive failure, capped at MaxPollErrorDelay.
    private TimeSpan BackoffFor(int consecutiveFailures)
    {
        double factor = Math.Pow(2, Math.Min(consecutiveFailures - 1, 30));
        return TimeSpan.FromTicks((long)Math.Min(options.PollErrorDelay.Ticks * factor, options.MaxPollErrorDelay.Ticks));
    }

    private async Task<bool> ProcessGuardedAsync(Message message)
    {
        try
        {
            return await ProcessAsync(message);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SqsLog.ProcessingFailed(logger, exception, endpoint.QueueName, message.MessageId);
            return false;
        }
    }

    private async Task<bool> ProcessAsync(Message message)
    {
        Envelope envelope;
        try
        {
            envelope = codec.Decode(SqsWire.Unpack(message.Body, message.MessageAttributes));
        }
        catch (UnreadableTransportMessage unreadable)
        {
            return await DeadLetterAsync(message, unreadable.Message);
        }

        ReceiveOutcome outcome;
        try
        {
            outcome = await receiver.ReceiveAsync(envelope, _processCts.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SqsLog.ReceiverFaulted(logger, exception, envelope.Id.Value);
            outcome = ReceiveOutcome.Nack;
        }

        return outcome switch
        {
            ReceiveOutcome.Ack => await DeleteAsync(message),
            ReceiveOutcome.Defer => await ReleaseAsync(message, 0),
            _ => await ReleaseAsync(message, (int)options.NackDelay.TotalSeconds),
        };
    }

    private async Task<bool> DeleteAsync(Message message)
    {
        try
        {
            await client.DeleteMessageAsync(
                new DeleteMessageRequest { QueueUrl = _queueUrl, ReceiptHandle = message.ReceiptHandle },
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SqsLog.AckFailed(logger, exception, endpoint.QueueName, message.MessageId);
            return false;
        }

        return true;
    }

    private async Task<bool> ReleaseAsync(Message message, int visibilitySeconds)
    {
        try
        {
            await client.ChangeMessageVisibilityAsync(
                new ChangeMessageVisibilityRequest
                {
                    QueueUrl = _queueUrl,
                    ReceiptHandle = message.ReceiptHandle,
                    VisibilityTimeout = visibilitySeconds,
                },
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SqsLog.ReleaseFailed(logger, exception, endpoint.QueueName, message.MessageId);
            return false;
        }

        return true;
    }

    // The source message is deleted only after the dead-letter write succeeded.
    private async Task<bool> DeadLetterAsync(Message message, string reason)
    {
        var attributes = (message.MessageAttributes ?? [])
            .Where(pair => pair.Key != SqsWire.DeadLetterReasonAttribute)
            .OrderBy(pair => pair.Key is SqsWire.HeadersAttribute or SqsWire.BodyEncodingAttribute ? 0 : 1)
            .Take(SqsWire.MaxAttributes - 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        attributes[SqsWire.DeadLetterReasonAttribute] = new MessageAttributeValue
        {
            DataType = "String",
            StringValue = reason.Length > 0 ? reason : "unreadable",
        };

        try
        {
            await client.SendMessageAsync(
                new SendMessageRequest { QueueUrl = _deadLetterUrl, MessageBody = message.Body, MessageAttributes = attributes },
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SqsLog.DeadLetterFailed(logger, exception, endpoint.QueueName, message.MessageId);
            return false;
        }

        SqsLog.DeadLettered(logger, endpoint.QueueName, message.MessageId, reason);
        return await DeleteAsync(message);
    }
}
