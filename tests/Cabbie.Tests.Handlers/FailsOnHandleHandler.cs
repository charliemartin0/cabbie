namespace Cabbie.Tests.Handlers;

/// <summary>
/// Always throws; used by the A5 prove-with to drive the MoveToErrorQueue policy
/// into the dead-letter store. Valid DX #2 signature, safe to scan with the other fixtures.
/// </summary>
public sealed record FailsOnHandleMessage(int OrderId);

public sealed class FailsOnHandleHandler
{
    public void Handle(FailsOnHandleMessage message) =>
        throw new TimeoutException("always fails");
}