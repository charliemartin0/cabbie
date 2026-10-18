using Microsoft.Extensions.Logging;

namespace Cabbie.Infrastructure.LocalQueues;

internal static partial class LocalQueueLog
{
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Local queue {Queue} is draining; deferred envelope {EnvelopeId} could not be put back and was dropped")]
    public static partial void DeferredEnvelopeDropped(ILogger logger, string queue, Guid envelopeId);
}
