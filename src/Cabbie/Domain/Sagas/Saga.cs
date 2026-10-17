namespace Cabbie.Domain.Sagas;

/// <summary>
/// Inert process-manager state. MarkCompleted means this instance is finished. Persistence and dispatch are not here.
/// </summary>
public abstract class Saga
{
    public bool IsCompleted { get; private set; }

    public void MarkCompleted()
    {
        IsCompleted = true;
    }
}
