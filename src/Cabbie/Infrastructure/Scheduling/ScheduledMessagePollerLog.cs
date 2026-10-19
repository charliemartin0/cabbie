using Microsoft.Extensions.Logging;

namespace Cabbie.Infrastructure.Scheduling;

internal static partial class ScheduledMessagePollerLog
{
    [LoggerMessage(
        EventId = 300,
        Level = LogLevel.Error,
        Message = "Scheduled message poll failed; retrying on the next tick")]
    public static partial void PollFailed(ILogger logger, Exception exception);
}
