using Microsoft.Extensions.Logging;

namespace Cabbie.Application.Execution;

internal static partial class ExecutorLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Dead-letter write failed for envelope {EnvelopeId} (handler {HandlerType})")]
    public static partial void DeadLetterWriteFailed(ILogger logger, Exception exception, Guid envelopeId, Type handlerType);
}
