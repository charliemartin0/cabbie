namespace Cabbie.Tests.Application.Discovery.Scanned;

public sealed record Ping;

public sealed class PingHandler
{
    public void Handle(Ping message)
    {
    }
}
