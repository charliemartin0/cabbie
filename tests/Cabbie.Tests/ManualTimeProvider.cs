namespace Cabbie.Tests;

/// <summary>
/// Deterministic <see cref="TimeProvider"/>: timers fire only when <see cref="Advance"/> moves time past them,
/// or immediately (on a pool thread) when <c>autoFire</c> is set, so delays cost no wall-clock time.
/// </summary>
public sealed class ManualTimeProvider(bool autoFire = false) : TimeProvider
{
    private readonly bool _autoFire = autoFire;
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _now += by;
            due = [.. _timers.Where(timer => timer.DueAt <= _now)];
        }

        foreach (ManualTimer timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset DueAt { get; private set; } = DateTimeOffset.MaxValue;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            bool fireNow = false;
            lock (owner._gate)
            {
                owner._timers.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    DueAt = DateTimeOffset.MaxValue;
                    return true;
                }

                DueAt = owner._now + dueTime;
                if (owner._autoFire)
                {
                    fireNow = true;
                }
                else
                {
                    owner._timers.Add(this);
                }
            }

            if (fireNow)
            {
                ThreadPool.QueueUserWorkItem(_ => callback(state));
            }

            return true;
        }

        public void Fire()
        {
            lock (owner._gate)
            {
                owner._timers.Remove(this);
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
