using Cabbie.Application.Scheduling;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Infrastructure.Scheduling;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Infrastructure.Scheduling;

public sealed class InMemoryScheduledEnvelopeHoldTests
{
    [Fact]
    public void peek_shows_parked_envelopes_without_removing_them()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var envelope = EnvelopeFactory.Create(deliverBy: DateTimeOffset.UtcNow.AddMinutes(1));

        hold.Park(envelope);

        Assert.Same(envelope, Assert.Single(hold.Peek()));
        Assert.Same(envelope, Assert.Single(hold.Peek()));
    }

    [Fact]
    public void empty_hold_peeks_nothing()
    {
        Assert.Empty(new InMemoryScheduledEnvelopeHold().Peek());
    }

    [Fact]
    public void overlapping_play_is_play_due_in_progress()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        using IDisposable first = hold.BeginPlay();

        Assert.Throws<PlayDueInProgress>(() => hold.BeginPlay());
    }

    [Fact]
    public void try_remove_drops_the_envelope_by_id()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var envelope = EnvelopeFactory.Create(id: new EnvelopeId(Guid.NewGuid()));
        hold.Park(envelope);

        Assert.True(hold.TryRemove(envelope.Id));
        Assert.Empty(hold.Peek());
        Assert.False(hold.TryRemove(envelope.Id));
    }
}
