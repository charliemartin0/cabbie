using Cabbie.Application.Scheduling;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Infrastructure.Scheduling;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Infrastructure.Scheduling;

public sealed class ScheduledMessagePollerTests
{
    private static readonly TimeSpan Cap = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task message_due_while_the_host_was_down_is_played_at_start()
    {
        var clock = new ManualTimeProvider();
        var hold = new InMemoryScheduledEnvelopeHold();
        var played = new List<EnvelopeId>();
        await hold.ParkAsync(EnvelopeFactory.Create(deliverBy: clock.GetUtcNow().AddMinutes(-1)));
        var scheduler = new SignallingScheduler(PlayInto(hold, played));
        using var poller = new ScheduledMessagePoller(scheduler, clock, new ScheduledMessagePollerOptions());

        await poller.StartAsync(CancellationToken.None);
        await scheduler.NextPlayAsync();
        await poller.StopAsync(CancellationToken.None);

        Assert.Single(played);
    }

    [Fact]
    public async Task message_is_played_only_after_the_clock_reaches_its_due_time()
    {
        var clock = new ManualTimeProvider();
        var hold = new InMemoryScheduledEnvelopeHold();
        var played = new List<EnvelopeId>();
        await hold.ParkAsync(EnvelopeFactory.Create(deliverBy: clock.GetUtcNow().AddMinutes(1)));
        var scheduler = new SignallingScheduler(PlayInto(hold, played));
        using var poller = new ScheduledMessagePoller(scheduler, clock, new ScheduledMessagePollerOptions());

        await poller.StartAsync(CancellationToken.None);
        await scheduler.NextPlayAsync();
        Assert.Empty(played);

        await AdvanceUntilNextPlayAsync(clock, scheduler);
        await poller.StopAsync(CancellationToken.None);

        Assert.Single(played);
    }

    [Fact]
    public async Task a_failing_play_does_not_stop_the_poller()
    {
        var clock = new ManualTimeProvider();
        int calls = 0;
        var scheduler = new SignallingScheduler(_ =>
            ++calls == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask);
        using var poller = new ScheduledMessagePoller(scheduler, clock, new ScheduledMessagePollerOptions());

        await poller.StartAsync(CancellationToken.None);
        await scheduler.NextPlayAsync();
        await AdvanceUntilNextPlayAsync(clock, scheduler);
        await poller.StopAsync(CancellationToken.None);

        Assert.True(calls >= 2);
    }

    [Fact]
    public void non_positive_poll_interval_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScheduledMessagePoller(
            new SignallingScheduler(_ => Task.CompletedTask),
            new ManualTimeProvider(),
            new ScheduledMessagePollerOptions { PollInterval = TimeSpan.Zero }));
    }

    private static Func<DateTimeOffset, Task> PlayInto(InMemoryScheduledEnvelopeHold hold, List<EnvelopeId> played)
    {
        var scheduler = new MessageScheduler(hold, (envelope, _) =>
        {
            played.Add(envelope.Id);
            return Task.CompletedTask;
        });
        return asOf => scheduler.PlayDue(asOf);
    }

    private static async Task AdvanceUntilNextPlayAsync(ManualTimeProvider clock, SignallingScheduler scheduler)
    {
        int target = scheduler.Plays + 1;
        using var cap = new CancellationTokenSource(Cap);
        while (scheduler.Plays < target)
        {
            // The poller may not have armed its delay yet; keep moving the clock until a tick lands.
            clock.Advance(TimeSpan.FromMinutes(2));
            await Task.Delay(TimeSpan.FromMilliseconds(10), cap.Token);
        }
    }

    private sealed class SignallingScheduler(Func<DateTimeOffset, Task> play) : IMessageScheduler
    {
        private int _plays;

        public int Plays => Volatile.Read(ref _plays);

        public async Task PlayDue(DateTimeOffset asOf, CancellationToken cancellationToken = default)
        {
            try
            {
                await play(asOf);
            }
            finally
            {
                Interlocked.Increment(ref _plays);
            }
        }

        public async Task NextPlayAsync()
        {
            using var cap = new CancellationTokenSource(Cap);
            while (Plays < 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), cap.Token);
            }
        }
    }
}
