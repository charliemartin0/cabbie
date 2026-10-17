namespace Cabbie.Application.Transports;

/// <summary>
/// What a transport does with an envelope after <see cref="IReceiver.ReceiveAsync"/>.
/// <see cref="Nack"/> is the zero value so an unset outcome never acknowledges.
/// </summary>
public enum ReceiveOutcome
{
    /// <summary>
    /// The executor finished with a fault the error policy did not resolve. The transport
    /// makes the envelope available for redelivery.
    /// </summary>
    Nack = 0,

    /// <summary>
    /// The executor finished and the envelope is done: handled, or its fault was resolved by
    /// the error policy (dead-lettered, discarded, requeued, parked for scheduled retry).
    /// The transport deletes it.
    /// </summary>
    Ack = 1,

    /// <summary>
    /// Processing was cancelled before the executor finished (the host is stopping). The
    /// transport releases the envelope without counting a failure.
    /// </summary>
    Defer = 2,
}
