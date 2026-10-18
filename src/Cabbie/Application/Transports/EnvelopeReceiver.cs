using Cabbie.Application.Bus;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cabbie.Application.Transports;

/// <summary>
/// The <see cref="IReceiver"/> every listener hands envelopes to: runs the envelope's
/// handlers through the configured dispatch and translates how that ended into a
/// <see cref="ReceiveOutcome"/>. Ack only after the executor finished; a fault the error
/// policy dead-lettered is also Ack; cancellation is Defer; any other fault is Nack and
/// is logged.
/// </summary>
public sealed class EnvelopeReceiver : IReceiver
{
    private readonly DispatchHandler _dispatch;
    private readonly ILogger _logger;

    public EnvelopeReceiver(MessageDelivery delivery, ILogger<EnvelopeReceiver>? logger = null)
        : this(
            (envelope, cancellationToken) => (delivery ?? throw new ArgumentNullException(nameof(delivery)))
                .Dispatch(envelope, scheduled: true, cancellationToken),
            logger)
    {
    }

    /// <summary>
    /// Dispatch hook for the host: inbound envelopes must route saga handlers through
    /// the saga orchestration (load/save), not the plain executor path.
    /// </summary>
    internal EnvelopeReceiver(DispatchHandler dispatch, ILogger<EnvelopeReceiver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        _dispatch = dispatch;
        _logger = logger ?? NullLogger<EnvelopeReceiver>.Instance;
    }

    public async Task<ReceiveOutcome> ReceiveAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        try
        {
            await _dispatch(envelope, cancellationToken);
            return ReceiveOutcome.Ack;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ReceiveOutcome.Defer;
        }
        catch (HandlerFault fault) when (fault.DeadLettered)
        {
            return ReceiveOutcome.Ack;
        }
        catch (Exception exception)
        {
            TransportLog.EnvelopeNacked(_logger, exception, envelope.Id.Value, envelope.Destination.Value);
            return ReceiveOutcome.Nack;
        }
    }
}
