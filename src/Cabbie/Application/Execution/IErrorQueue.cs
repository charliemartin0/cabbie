using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Execution;

/// <summary>
/// Port for envelopes whose Invoke retries are exhausted. Persistence implements the durable table later.
/// </summary>
public interface IErrorQueue
{
    /// <summary>
    /// Move an exhausted envelope to the dead-letter destination, carrying the
    /// exception that exhausted the retries as the cause. The executor awaits the write;
    /// a failure surfaces as <see cref="DeadLetterWriteFailed"/> inside a <see cref="HandlerFault"/>.
    /// </summary>
    ValueTask MoveAsync(Envelope envelope, Exception cause, CancellationToken cancellationToken = default);
}
