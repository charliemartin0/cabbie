using Microsoft.Extensions.Logging;

namespace Cabbie.Infrastructure.LocalQueues;

internal static partial class LocalQueueLog
{
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Local queue {Queue} is draining; deferred envelope {EnvelopeId} could not be put back and was dropped")]
    public static partial void DeferredEnvelopeDropped(ILogger logger, string queue, Guid envelopeId);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Error,
        Message = "Local queue {Queue} received an unreadable message; it was acknowledged and dead-lettered")]
    public static partial void UnreadableMessageDeadLettered(ILogger logger, Exception exception, string queue);
}
