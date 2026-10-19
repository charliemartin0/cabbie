using Cabbie.Application.Scheduling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cabbie.Infrastructure.Scheduling;

/// <summary>
/// Plays due scheduled messages on a timer driven by the host <see cref="TimeProvider"/>: once at
/// start (so messages that came due while the host was down are delivered), then every
/// <see cref="ScheduledMessagePollerOptions.PollInterval"/>. A failing play is logged and retried on
/// the next tick; only host shutdown stops the poller.
/// </summary>
public sealed class ScheduledMessagePoller : BackgroundService
{
    private readonly IMessageScheduler _scheduler;
    private readonly TimeProvider _time;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;

    public ScheduledMessagePoller(
        IMessageScheduler scheduler,
        TimeProvider timeProvider,
        ScheduledMessagePollerOptions options,
        ILogger<ScheduledMessagePoller>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PollInterval, TimeSpan.Zero);
        _scheduler = scheduler;
        _time = timeProvider;
        _interval = options.PollInterval;
        _logger = logger ?? NullLogger<ScheduledMessagePoller>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                await PlayAsync(stoppingToken);
                await Task.Delay(_interval, _time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host stop: leave quietly.
        }
    }

    private async Task PlayAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _scheduler.PlayDue(_time.GetUtcNow(), stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            ScheduledMessagePollerLog.PollFailed(_logger, exception);
        }
    }
}
