using Cabbie.Application.Scheduling;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Infrastructure.Scheduling;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Infrastructure.Scheduling;

public sealed class InMemoryScheduledEnvelopeHoldTests
{
    [Fact]
    public async Task peek_shows_parked_envelopes_without_removing_them()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var envelope = EnvelopeFactory.Create(deliverBy: DateTimeOffset.UtcNow.AddMinutes(1));

        await hold.ParkAsync(envelope);

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
    public async Task claimed_envelopes_are_hidden_from_a_second_claim()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        DateTimeOffset due = DateTimeOffset.UtcNow;
        await hold.ParkAsync(EnvelopeFactory.Create(deliverBy: due));

        Assert.Single(await hold.ClaimDueAsync(due));
        Assert.Empty(await hold.ClaimDueAsync(due));
    }

    [Fact]
    public async Task envelopes_not_yet_due_are_not_claimed()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        DateTimeOffset due = DateTimeOffset.UtcNow;
        await hold.ParkAsync(EnvelopeFactory.Create(deliverBy: due));

        Assert.Empty(await hold.ClaimDueAsync(due.AddSeconds(-1)));
        Assert.Single(hold.Peek());
    }

    [Fact]
    public async Task released_envelopes_are_claimable_again()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        DateTimeOffset due = DateTimeOffset.UtcNow;
        await hold.ParkAsync(EnvelopeFactory.Create(deliverBy: due));
        ClaimedEnvelope claim = Assert.Single(await hold.ClaimDueAsync(due));

        await hold.ReleaseAsync(claim);

        Assert.Equal(claim.Envelope.Id, Assert.Single(await hold.ClaimDueAsync(due)).Envelope.Id);
    }

    [Fact]
    public async Task completed_envelopes_never_return()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        DateTimeOffset due = DateTimeOffset.UtcNow;
        await hold.ParkAsync(EnvelopeFactory.Create(deliverBy: due));
        ClaimedEnvelope claim = Assert.Single(await hold.ClaimDueAsync(due));

        await hold.CompleteAsync(claim);
        await hold.ReleaseAsync(claim);

        Assert.Empty(await hold.ClaimDueAsync(due));
        Assert.Empty(hold.Peek());
    }
}
