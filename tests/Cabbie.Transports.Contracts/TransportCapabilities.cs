namespace Cabbie.Transports.Contracts;

/// <summary>
/// The documented behaviours a transport is allowed to differ on. Everything else in
/// <see cref="TransportContract"/> is identical for every transport.
/// </summary>
/// <param name="Nack">What the transport does with an envelope the receiver nacks.</param>
/// <param name="SharedAddress">What two listeners on one address do.</param>
public sealed record TransportCapabilities(NackBehaviour Nack, SharedAddressBehaviour SharedAddress);

/// <summary>What a transport does with a nacked envelope.</summary>
public enum NackBehaviour
{
    /// <summary>
    /// Local: no redelivery. The error policy already ran and the receiver logged the Nack.
    /// </summary>
    Drop,

    /// <summary>A broker that makes the message visible again (SQS).</summary>
    Redeliver,
}

/// <summary>What a transport does when two listeners share one address.</summary>
public enum SharedAddressBehaviour
{
    /// <summary>
    /// Local: one receiver per address. A second listener throws
    /// <see cref="InvalidOperationException"/> at create or start.
    /// </summary>
    Rejected,

    /// <summary>Each message goes to exactly one of the listeners (SQS).</summary>
    CompetingConsumers,
}
