using System.Text.Json;
using Cabbie.Application.Scheduling;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace Cabbie.Postgresql;

/// <summary>
/// Postgres-backed <see cref="IScheduledEnvelopeHold"/>. Envelopes are stored in
/// <c>cabbie_scheduled</c> in their transport form and survive host restart. A claim leases due rows
/// with <c>FOR UPDATE SKIP LOCKED</c> and a <c>locked_until</c> deadline, so concurrent pollers never
/// receive the same row and a poller that dies mid-play releases its rows when the lease expires
/// (at-least-once; the inbox deduplicates the redelivery).
/// </summary>
public sealed class PostgresScheduledEnvelopeHold : IScheduledEnvelopeHold
{
    private const string ClaimSql = """
        UPDATE cabbie_scheduled SET locked_until = @until
        WHERE seq IN (
            SELECT seq FROM cabbie_scheduled
            WHERE due_at <= @asof AND (locked_until IS NULL OR locked_until <= @now)
            ORDER BY due_at, seq
            LIMIT @n
            FOR UPDATE SKIP LOCKED)
        RETURNING seq, due_at, headers, body
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly EnvelopeCodec _codec;
    private readonly TimeSpan _lease;
    private readonly int _batchSize;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    public PostgresScheduledEnvelopeHold(
        NpgsqlDataSource dataSource,
        EnvelopeCodec codec,
        PostgresScheduleOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<PostgresScheduledEnvelopeHold>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(codec);
        PostgresScheduleOptions resolved = options ?? new PostgresScheduleOptions();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(resolved.ClaimLease, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(resolved.BatchSize, 0);
        _dataSource = dataSource;
        _codec = codec;
        _lease = resolved.ClaimLease;
        _batchSize = resolved.BatchSize;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<PostgresScheduledEnvelopeHold>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask ParkAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        DateTimeOffset due = envelope.DeliverBy.Value
            ?? throw new ArgumentException("A scheduled envelope needs a delivery time.", nameof(envelope));
        TransportMessage message = _codec.Encode(envelope);

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "INSERT INTO cabbie_scheduled (id, due_at, headers, body) VALUES (@id, @due, @headers::jsonb, @body)",
            conn);
        cmd.Parameters.AddWithValue("id", envelope.Id.Value);
        cmd.Parameters.AddWithValue("due", due.ToUniversalTime());
        cmd.Parameters.AddWithValue("headers", JsonSerializer.Serialize(message.Headers));
        cmd.Parameters.Add(new NpgsqlParameter("body", NpgsqlDbType.Bytea) { Value = message.Body.ToArray() });
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ClaimedEnvelope>> ClaimDueAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        List<(long Seq, DateTimeOffset Due, Dictionary<string, string> Headers, byte[] Body)> rows = [];

        await using (NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(cancellationToken))
        await using (NpgsqlCommand cmd = new NpgsqlCommand(ClaimSql, conn))
        {
            cmd.Parameters.AddWithValue("until", (now + _lease).ToUniversalTime());
            cmd.Parameters.AddWithValue("asof", asOf.ToUniversalTime());
            cmd.Parameters.AddWithValue("now", now.ToUniversalTime());
            cmd.Parameters.AddWithValue("n", _batchSize);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                Dictionary<string, string> headers =
                    JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(2)) ?? [];
                rows.Add((reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), headers, reader.GetFieldValue<byte[]>(3)));
            }
        }

        List<ClaimedEnvelope> claimed = new(rows.Count);
        foreach ((long seq, _, Dictionary<string, string> headers, byte[] body) in rows.OrderBy(row => row.Due).ThenBy(row => row.Seq))
        {
            try
            {
                claimed.Add(new ClaimedEnvelope(_codec.Decode(new TransportMessage(headers, body)), seq));
            }
            catch (UnreadableTransportMessage unreadable)
            {
                // Stays locked: it becomes claimable again when the lease expires, so a later deploy that
                // knows the message type picks it up, and it never blocks the rows behind it.
                PostgresScheduleLog.UnreadableScheduledEnvelope(_logger, seq, unreadable.Message);
            }
        }

        return claimed;
    }

    /// <inheritdoc />
    public async ValueTask CompleteAsync(ClaimedEnvelope claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = new NpgsqlCommand("DELETE FROM cabbie_scheduled WHERE seq = @seq", conn);
        cmd.Parameters.AddWithValue("seq", claim.Token);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(ClaimedEnvelope claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "UPDATE cabbie_scheduled SET locked_until = NULL WHERE seq = @seq",
            conn);
        cmd.Parameters.AddWithValue("seq", claim.Token);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Overlapping pollers are safe by design, so there is no play session to guard.</summary>
    public IDisposable BeginPlay() => NoPlaySession.Instance;

    private sealed class NoPlaySession : IDisposable
    {
        public static readonly NoPlaySession Instance = new();

        public void Dispose()
        {
        }
    }
}
