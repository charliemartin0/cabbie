using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cabbie.EntityFrameworkCore;

/// <summary>
/// Sends unsent outbox rows through an <see cref="ISender"/> and marks each one as it goes. A crash between
/// a send and its mark re-sends that one row on the next pass: delivery is at-least-once, and the
/// receiving inbox dedupes by envelope id. Rows that cannot be decoded are dead-lettered so they stop
/// filling the batch.
/// </summary>
public sealed class OutboxRelay<TContext>
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ISender _sender;
    private readonly EnvelopeCodec _codec;
    private readonly TimeProvider _time;
    private readonly int _batchSize;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryBaseDelay;
    private readonly ILogger _logger;

    public OutboxRelay(
        IServiceScopeFactory scopes,
        ISender sender,
        EnvelopeCodec codec,
        TimeProvider time,
        EfOutboxRelayOptions options,
        ILogger<OutboxRelay<TContext>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.BatchSize, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxAttempts, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.RetryBaseDelay, TimeSpan.Zero);
        _scopes = scopes;
        _sender = sender;
        _codec = codec;
        _time = time;
        _batchSize = options.BatchSize;
        _maxAttempts = options.MaxAttempts;
        _retryBaseDelay = options.RetryBaseDelay;
        _logger = logger ?? NullLogger<OutboxRelay<TContext>>.Instance;
    }

    /// <summary>
    /// Send one batch of due rows, oldest first. A row whose send throws is retried with exponential backoff
    /// and dead-lettered at <see cref="EfOutboxRelayOptions.MaxAttempts"/>; other rows keep relaying.
    /// Returns how many were sent and marked.
    /// </summary>
    public async Task<int> RelayOnceAsync(CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _scopes.CreateScope();
        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();
        DateTimeOffset now = _time.GetUtcNow();
        List<OutboxMessage> rows = await context.Set<OutboxMessage>()
            .Where(message => message.SentAt == null && message.DeadLetteredAt == null
                && (message.NextAttemptAt == null || message.NextAttemptAt <= now))
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(_batchSize)
            .ToListAsync(cancellationToken);

        int sent = 0;
        foreach (OutboxMessage row in rows)
        {
            Envelope envelope;
            try
            {
                envelope = OutboxMessageMapper.ToEnvelope(row, _codec);
            }
            catch (UnreadableTransportMessage exception)
            {
                row.DeadLetteredAt = _time.GetUtcNow();
                await context.SaveChangesAsync(cancellationToken);
                OutboxRelayLog.RowUnreadable(_logger, row.Id, exception);
                continue;
            }

            try
            {
                await _sender.SendAsync(envelope, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                row.Attempts++;
                if (row.Attempts >= _maxAttempts)
                {
                    row.DeadLetteredAt = _time.GetUtcNow();
                    OutboxRelayLog.RowDeadLettered(_logger, row.Id, row.Attempts, exception);
                }
                else
                {
                    double factor = Math.Pow(2, Math.Min(row.Attempts - 1, 20));
                    row.NextAttemptAt = _time.GetUtcNow() + (_retryBaseDelay * factor);
                    OutboxRelayLog.RowSendFailed(_logger, row.Id, row.Attempts, exception);
                }

                await context.SaveChangesAsync(cancellationToken);
                continue;
            }

            row.SentAt = _time.GetUtcNow();
            await context.SaveChangesAsync(cancellationToken);
            sent++;
        }

        return sent;
    }

    /// <summary>
    /// Puts every dead-lettered, unsent row back in the relay: clears <c>DeadLetteredAt</c>, resets
    /// <c>Attempts</c> and <c>NextAttemptAt</c>. Run it after fixing the cause (register the missing transport,
    /// deploy a host that knows the message type); rows that are still unreadable are dead-lettered again on
    /// the next pass. Returns how many rows were requeued. Equivalent SQL:
    /// <c>UPDATE cabbie_ef_outbox SET "DeadLetteredAt" = NULL, "Attempts" = 0, "NextAttemptAt" = NULL
    /// WHERE "DeadLetteredAt" IS NOT NULL AND "SentAt" IS NULL</c>.
    /// </summary>
    public async Task<int> RedriveDeadLettersAsync(CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _scopes.CreateScope();
        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();
        return await context.Set<OutboxMessage>()
            .Where(message => message.DeadLetteredAt != null && message.SentAt == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(message => message.DeadLetteredAt, (DateTimeOffset?)null)
                    .SetProperty(message => message.Attempts, 0)
                    .SetProperty(message => message.NextAttemptAt, (DateTimeOffset?)null),
                cancellationToken);
    }
}
