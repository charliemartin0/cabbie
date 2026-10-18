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
        Message = "Outbox row {OutboxId} is unreadable; leaving it unsent")]
    public static partial void RowUnreadable(ILogger logger, Guid outboxId, Exception exception);
}
