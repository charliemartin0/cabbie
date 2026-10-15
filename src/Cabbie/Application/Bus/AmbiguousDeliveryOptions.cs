namespace Cabbie.Application.Bus;

/// <summary>
/// DeliveryOptions set both a relative delay and an absolute Until.
/// </summary>
public sealed class AmbiguousDeliveryOptions : Exception
{
    public AmbiguousDeliveryOptions()
        : base("DeliveryOptions cannot set both Delay and Until.")
    {
    }
}
