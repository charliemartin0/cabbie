using Cabbie.Domain.Sagas.ValueObjects;

namespace Cabbie.Application.Sagas;

/// <summary>
/// A saga handler ran with a correlatable message whose identity value was empty.
/// </summary>
public sealed class SagaIdRequired : Exception
{
    public Type SagaType { get; }

    public Type MessageType { get; }

    public SagaIdRequired(Type sagaType, Type messageType)
        : base($"Saga '{sagaType}' requires a non-empty saga id on '{messageType}'.")
    {
        ArgumentNullException.ThrowIfNull(sagaType);
        ArgumentNullException.ThrowIfNull(messageType);
        SagaType = sagaType;
        MessageType = messageType;
    }
}
