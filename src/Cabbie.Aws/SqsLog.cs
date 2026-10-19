using Microsoft.Extensions.Logging;

namespace Cabbie.Aws;

internal static partial class SqsLog
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Error, Message = "Receiving from SQS queue {Queue} failed; retrying after a pause")]
    public static partial void ReceiveFailed(ILogger logger, Exception exception, string queue);

    [LoggerMessage(EventId = 101, Level = LogLevel.Error, Message = "Receiver faulted on envelope {EnvelopeId}; treating it as a nack")]
    public static partial void ReceiverFaulted(ILogger logger, Exception exception, Guid envelopeId);

    [LoggerMessage(EventId = 102, Level = LogLevel.Error, Message = "Deleting SQS message {MessageId} from {Queue} failed; it will be redelivered")]
    public static partial void AckFailed(ILogger logger, Exception exception, string queue, string messageId);

    [LoggerMessage(EventId = 103, Level = LogLevel.Error, Message = "Releasing SQS message {MessageId} on {Queue} failed; it will be redelivered after its visibility timeout")]
    public static partial void ReleaseFailed(ILogger logger, Exception exception, string queue, string messageId);

    [LoggerMessage(EventId = 104, Level = LogLevel.Warning, Message = "Unreadable SQS message {MessageId} from {Queue} moved to the dead-letter queue: {Reason}")]
    public static partial void DeadLettered(ILogger logger, string queue, string messageId, string reason);

    [LoggerMessage(EventId = 105, Level = LogLevel.Error, Message = "Dead-lettering SQS message {MessageId} from {Queue} failed; the source message is kept and will be retried")]
    public static partial void DeadLetterFailed(ILogger logger, Exception exception, string queue, string messageId);

    [LoggerMessage(EventId = 106, Level = LogLevel.Error, Message = "Processing SQS message {MessageId} from {Queue} failed unexpectedly; the message is left for redelivery")]
    public static partial void ProcessingFailed(ILogger logger, Exception exception, string queue, string messageId);
}
