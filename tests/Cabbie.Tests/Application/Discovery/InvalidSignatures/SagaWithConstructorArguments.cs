using Cabbie.Domain.Sagas;

namespace Cabbie.Tests.Application.Discovery.InvalidSignatures;

public sealed record ConstructorArgsSagaStart([property: SagaIdentity] int SagaId);

/// <summary>
/// A saga whose constructor needs arguments. Saga state is created and loaded by Cabbie, so
/// Scan rejects it; services belong in handler method parameters.
/// </summary>
public sealed class SagaWithConstructorArguments(string name) : Saga
{
    public string Name { get; } = name;

    public void Start(ConstructorArgsSagaStart message)
    {
    }
}
