namespace Cabbie.Application.Transports;

/// <summary>
/// Inbound half of a transport endpoint, started and stopped by the host. A listener pulls
/// (or is pushed) envelopes from its address and passes each to its <see cref="IReceiver"/>,
/// then acts on the <see cref="ReceiveOutcome"/>.
/// </summary>
public interface IListener
{
    /// <summary>The endpoint this listener receives from.</summary>
    Uri Address { get; }

    /// <summary>Begin receiving. Returns once the listener is accepting envelopes.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stop taking new envelopes and wait for the ones already accepted to finish.
    /// Cancelling <paramref name="cancellationToken"/> abandons the wait.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);
}
