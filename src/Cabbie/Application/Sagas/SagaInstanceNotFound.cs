using Cabbie.Domain.Sagas.ValueObjects;

namespace Cabbie.Application.Sagas;

/// <summary>
/// A Handle-bound saga message arrived for a missing or completed instance and the saga has no NotFound method.
/// </summary>
public sealed class SagaInstanceNotFound : Exception
{
    public Type SagaType { get; }

    public SagaId SagaId { get; }

    public Type MessageType { get; }

    public SagaInstanceNotFound(Type sagaType, SagaId sagaId, Type messageType)
        : base($"Saga '{sagaType}' instance '{sagaId.Value}' was not found for '{messageType}'.")
    {
        ArgumentNullException.ThrowIfNull(sagaType);
        ArgumentNullException.ThrowIfNull(sagaId);
        ArgumentNullException.ThrowIfNull(messageType);
        SagaType = sagaType;
        SagaId = sagaId;
        MessageType = messageType;
    }
}
