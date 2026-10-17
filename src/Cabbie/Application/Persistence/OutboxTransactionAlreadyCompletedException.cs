namespace Cabbie.Application.Persistence;

/// <summary>
/// Thrown when a single-use <see cref="IOutboxTransaction"/> is committed or rolled back a
/// second time. Catch this type specifically when a handler or saga may have already
/// completed the transaction; any other <see cref="InvalidOperationException"/> is a real
/// failure and must not be swallowed.
/// </summary>
public sealed class OutboxTransactionAlreadyCompletedException : InvalidOperationException
{
    public OutboxTransactionAlreadyCompletedException()
        : base("This outbox transaction has already been committed or rolled back.")
    {
    }
}