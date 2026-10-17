namespace Cabbie.Tests.InvalidHandlers;

/// <summary>
/// A deliberately invalid handler: the second parameter is a by-ref slot that no container
/// can fill. Scan must reject it at discovery time. Lives in its own assembly so no other
/// test's host scan touches it.
/// </summary>
public sealed record BadRefCommand(int OrderId);

public sealed class BadRefHandler
{
    public void Handle(BadRefCommand message, ref int count)
    {
    }
}
