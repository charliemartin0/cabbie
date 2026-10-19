using Microsoft.Extensions.Logging;

namespace Cabbie.EntityFrameworkCore;

internal static partial class OutboxRelayLog
{
    [LoggerMessage(
        EventId = 400,
        Level = LogLevel.Error,
        Message = "Outbox relay pass failed; retrying on the next tick")]
    public static partial void PassFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 401,
        Level = LogLevel.Warning,
        Message = "Outbox row {OutboxId} is unreadable; dead-lettered and skipped from now on")]
    public static partial void RowUnreadable(ILogger logger, Guid outboxId, Exception exception);

    [LoggerMessage(
        EventId = 402,
        Level = LogLevel.Warning,
        Message = "Outbox row {OutboxId} failed send attempt {Attempt}; retrying after backoff")]
    public static partial void RowSendFailed(ILogger logger, Guid outboxId, int attempt, Exception exception);

    [LoggerMessage(
        EventId = 403,
        Level = LogLevel.Error,
        Message = "Outbox row {OutboxId} failed {Attempt} send attempts; dead-lettered")]
    public static partial void RowDeadLettered(ILogger logger, Guid outboxId, int attempt, Exception exception);
}
