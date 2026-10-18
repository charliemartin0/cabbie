namespace Cabbie.Transports.Contracts;

/// <summary>
/// How long the suite waits. A broker is slower than an in-process queue, so each fixture
/// states its own numbers.
/// </summary>
/// <param name="DeliveryTimeout">
/// Bounds every wait for something that must happen: delivery, redelivery, dead-letter, a
/// stop completing.
/// </param>
/// <param name="QuietWindow">
/// How long the suite watches for something that must not happen: redelivery after an ack,
/// delivery after a stop, a second dead-letter. A broker fixture must set it above its
/// redelivery delay (the visibility timeout), or those checks prove nothing.
/// </param>
public sealed record TransportContractTimings(TimeSpan DeliveryTimeout, TimeSpan QuietWindow)
{
    public static TransportContractTimings InProcess { get; } =
        new(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(250));
}
