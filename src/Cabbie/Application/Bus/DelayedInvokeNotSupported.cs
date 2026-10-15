namespace Cabbie.Application.Bus;

/// <summary>
/// Invoke never waits for DeliverBy. Pass delay only on Publish or cascade.
/// </summary>
public sealed class DelayedInvokeNotSupported : Exception
{
    public DelayedInvokeNotSupported()
        : base("Invoke is always now. Delay and Until are not supported on InvokeAsync.")
    {
    }
}
