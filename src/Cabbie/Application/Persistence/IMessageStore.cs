namespace Cabbie.Application.Persistence;

/// <summary>
/// Composition of outbox, inbox, and dead letter. Recovery on host start is the outbox
/// pending list plus the inbox in-flight list combined.
/// </summary>
public interface IMessageStore
{
    /// <summary>
    /// Outgoing envelopes staged by handlers and replayed on start.
    /// </summary>
    IOutboxStore Outbox { get; }

    /// <summary>
    /// Incoming envelopes acquired for exactly-once processing.
    /// </summary>
    IInboxStore Inbox { get; }

    /// <summary>
    /// Envelopes whose retries were exhausted.
    /// </summary>
    IDeadLetterStore DeadLetter { get; }
}
