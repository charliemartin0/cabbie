using Cabbie.Application.Execution;
using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;

namespace Cabbie.Infrastructure.Persistence;

/// <summary>
/// Wires the execution port to the persistence port: <see cref="IErrorQueue.MoveAsync"/>
/// records the envelope and its cause in the <see cref="IDeadLetterStore"/>.
/// </summary>
internal sealed class DeadLetterQueueAdapter : IErrorQueue
{
    private readonly IDeadLetterStore _store;

    public DeadLetterQueueAdapter(IDeadLetterStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public ValueTask MoveAsync(Envelope envelope, Exception cause, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(cause);
        return _store.RecordAsync(envelope, cause, cancellationToken);
    }
}
