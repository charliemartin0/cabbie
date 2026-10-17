using Cabbie.Domain.Sagas;

namespace Cabbie.Tests.Handlers;

/// <summary>
/// Starts a saga instance in the store so the queue-dispatch prove-with has a row to load.
/// </summary>
public sealed record QueueSagaStart([property: SagaIdentity] int SagaId, string Payload);

/// <summary>
/// Handled by <see cref="QueueSaga"/> through the local queue; mutates saga state that
/// must be persisted via ISagaStore (the plain executor path would drop it).
/// </summary>
public sealed record SagasViaQueueMessage([property: SagaIdentity] int SagaId, string Payload);

public sealed class QueueSaga : Saga
{
    private static TaskCompletionSource _handled = New();

    public static Task Handled => _handled.Task;

    public static void Reset() => _handled = New();

    public string? Status { get; set; } = "started";

    public void Start(QueueSagaStart message)
    {
        Status = message.Payload;
    }

    public void Handle(SagasViaQueueMessage message)
    {
        Status = message.Payload;
        _handled.TrySetResult();
    }

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}