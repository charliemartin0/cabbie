using Microsoft.Extensions.Logging;

namespace Cabbie.Postgresql;

internal static partial class PostgresScheduleLog
{
    [LoggerMessage(
        EventId = 200,
        Level = LogLevel.Warning,
        Message = "Scheduled message row {Seq} is unreadable and stays locked until its lease expires: {Reason}")]
    public static partial void UnreadableScheduledEnvelope(ILogger logger, long seq, string reason);
}
