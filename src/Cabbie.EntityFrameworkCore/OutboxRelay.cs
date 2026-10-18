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
/// receiving inbox dedupes by envelope id.
/// </summary>
public sealed class OutboxRelay<TContext>
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ISender _sender;
    private readonly EnvelopeCodec _codec;
    private readonly TimeProvider _time;
    private readonly int _batchSize;
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
        _scopes = scopes;
        _sender = sender;
        _codec = codec;
        _time = time;
        _batchSize = options.BatchSize;
        _logger = logger ?? NullLogger<OutboxRelay<TContext>>.Instance;
    }

    /// <summary>Send one batch of unsent rows, oldest first. Returns how many were sent and marked.</summary>
    public async Task<int> RelayOnceAsync(CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _scopes.CreateScope();
        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();
        List<OutboxMessage> rows = await context.Set<OutboxMessage>()
            .Where(message => message.SentAt == null)
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
                OutboxRelayLog.RowUnreadable(_logger, row.Id, exception);
                continue;
            }

            await _sender.SendAsync(envelope, cancellationToken);
            row.SentAt = _time.GetUtcNow();
            await context.SaveChangesAsync(cancellationToken);
            sent++;
        }

        return sent;
    }
}
