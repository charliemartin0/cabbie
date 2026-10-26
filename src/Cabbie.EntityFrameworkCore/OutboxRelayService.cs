using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cabbie.EntityFrameworkCore;

/// <summary>
/// Runs <see cref="OutboxRelay{TContext}"/> once at start and then every poll interval. A failing pass is
/// logged and retried on the next tick; only host shutdown stops it.
/// </summary>
public sealed class OutboxRelayService<TContext> : BackgroundService
    where TContext : DbContext
{
    private readonly OutboxRelay<TContext> _relay;
    private readonly TimeProvider _time;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;

    public OutboxRelayService(
        OutboxRelay<TContext> relay,
        TimeProvider time,
        EfOutboxRelayOptions options,
        ILogger<OutboxRelayService<TContext>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PollInterval, TimeSpan.Zero);
        _relay = relay;
        _time = time;
        _interval = options.PollInterval;
        _logger = logger ?? NullLogger<OutboxRelayService<TContext>>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                await PassAsync(stoppingToken);
                await Task.Delay(_interval, _time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host stop: leave quietly.
        }
    }

    private async Task PassAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _relay.RelayOnceAsync(stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            OutboxRelayLog.PassFailed(_logger, exception);
        }
    }
}
