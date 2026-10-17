using Cabbie.Domain.Sagas.ValueObjects;

namespace Cabbie.Application.Sagas;

/// <summary>
/// Start ran and the store already has this saga type and id (open or completed).
/// </summary>
public sealed class SagaAlreadyExists : Exception
{
    public Type SagaType { get; }

    public SagaId SagaId { get; }

    public SagaAlreadyExists(Type sagaType, SagaId sagaId)
        : base($"Saga '{sagaType}' already exists for id '{sagaId.Value}'.")
    {
        ArgumentNullException.ThrowIfNull(sagaType);
        ArgumentNullException.ThrowIfNull(sagaId);
        SagaType = sagaType;
        SagaId = sagaId;
    }
}
