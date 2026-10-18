using Microsoft.Extensions.Logging;

namespace Cabbie.Application.Transports;

internal static partial class TransportLog
{
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Envelope {EnvelopeId} for {Destination} failed and was not acknowledged; the error policy did not resolve the fault")]
    public static partial void EnvelopeNacked(ILogger logger, Exception exception, Guid envelopeId, Uri destination);
}
