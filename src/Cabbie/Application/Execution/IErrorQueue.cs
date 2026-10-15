using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Execution;

/// <summary>
/// Port for envelopes whose Invoke retries are exhausted. Persistence implements the durable table later.
/// </summary>
public interface IErrorQueue
{
    /// <summary>
    /// Move an exhausted envelope to the dead-letter destination, carrying the
    /// exception that exhausted the retries as the cause.
    /// </summary>
    void Move(Envelope envelope, Exception cause);
}