namespace Cabbie.Tests.FactoryHandlers;

public sealed record FactoryBuiltMessage;

/// <summary>
/// A scanned handler the container cannot build by type (string is not a service): only an
/// application registration with a factory can supply it. Lives in its own assembly because every
/// handler an assembly holds is scanned, and this one must not fail startup for other fixtures.
/// </summary>
public sealed class FactoryBuiltHandler(string origin, List<string> sink)
{
    public void Handle(FactoryBuiltMessage message) => sink.Add(origin);
}
