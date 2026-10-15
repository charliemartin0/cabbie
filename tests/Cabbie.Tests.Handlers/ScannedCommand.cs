namespace Cabbie.Tests.Handlers;

/// <summary>
/// A plain user message with a handler in the fixture assembly, used to prove
/// UseCabbie scans HandlerAssemblies without a manual Scan call.
/// </summary>
public sealed record ScannedCommand(int OrderId);

public sealed class ScannedCommandHandler
{
    public void Handle(ScannedCommand message)
    {
    }
}