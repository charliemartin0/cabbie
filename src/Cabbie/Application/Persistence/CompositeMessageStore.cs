namespace Cabbie.Application.Persistence;

/// <summary>
/// A delegating <see cref="IMessageStore"/> that composes an outbox, inbox, and
/// dead-letter store from independent port implementations. Enables the postgres
/// adapter to bind a Postgres outbox against in-memory inbox/dead-letter without
/// requiring Postgres implementations of every sub-port.
/// </summary>
public sealed class CompositeMessageStore : IMessageStore
{
    public CompositeMessageStore(
        IOutboxStore outbox,
        IInboxStore inbox,
        IDeadLetterStore deadLetter)
    {
        Outbox = outbox;
        Inbox = inbox;
        DeadLetter = deadLetter;
    }

    public IOutboxStore Outbox { get; }
    public IInboxStore Inbox { get; }
    public IDeadLetterStore DeadLetter { get; }
}