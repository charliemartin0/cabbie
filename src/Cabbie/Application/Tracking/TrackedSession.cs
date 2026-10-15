using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Tracking;

public sealed record ExecutedRecord(object Message, Envelope Envelope, int Attempt, Exception? Exception);

public sealed record PublishedRecord(object Message);

public sealed record ScheduledRecord(Envelope Envelope);

/// <summary>
/// One-shot test session: published / executed / scheduled bags. PlayScheduled returns a new session.
/// </summary>
public sealed class TrackedSession
{
    private readonly Func<DateTimeOffset, CancellationToken, Task<TrackedSession>> _play;

    public TrackedSession(
        IReadOnlyList<ExecutedRecord> executed,
        IReadOnlyList<PublishedRecord> published,
        IReadOnlyList<ScheduledRecord> scheduled,
        Func<DateTimeOffset, CancellationToken, Task<TrackedSession>> play)
    {
        ArgumentNullException.ThrowIfNull(executed);
        ArgumentNullException.ThrowIfNull(published);
        ArgumentNullException.ThrowIfNull(scheduled);
        ArgumentNullException.ThrowIfNull(play);
        Executed = executed;
        Published = published;
        Scheduled = scheduled;
        _play = play;
    }

    public IReadOnlyList<ExecutedRecord> Executed { get; }

    public IReadOnlyList<PublishedRecord> Published { get; }

    public IReadOnlyList<ScheduledRecord> Scheduled { get; }

    public Task<TrackedSession> PlayScheduledMessagesAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default) =>
        _play(asOf, cancellationToken);
}
