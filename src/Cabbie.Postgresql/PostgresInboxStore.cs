using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope.ValueObjects;
using Npgsql;

namespace Cabbie.Postgresql;

/// <summary>
/// Postgres-backed <see cref="IInboxStore"/>. Acquires an incoming (envelope id, handler) pair exactly once
/// via an atomic <c>INSERT ... ON CONFLICT DO NOTHING</c>; the row keeps the id until it is
/// marked processed (surviving host restart, so recovery can re-run in-flight work) or
/// released back for retry.
/// <para>
/// One connection per operation, mirroring <see cref="PostgresOutboxStore"/>.
/// </para>
/// </summary>
public sealed class PostgresInboxStore : IInboxStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresInboxStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    /// <inheritdoc />
    /// <remarks>Pre-upgrade rows carry handler '' and match any handler for that envelope id.</remarks>
    public async ValueTask<bool> TryAcquireAsync(EnvelopeId id, HandlerName handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(handler);

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "INSERT INTO cabbie_inbox (id, handler, status) SELECT @id, @handler, 'in_flight' WHERE NOT EXISTS (SELECT 1 FROM cabbie_inbox WHERE id = @id AND handler = '') ON CONFLICT (id, handler) DO NOTHING",
            conn);
        cmd.Parameters.AddWithValue("id", id.Value);
        cmd.Parameters.AddWithValue("handler", handler.Value);
        int inserted = await cmd.ExecuteNonQueryAsync(ct);
        return inserted == 1;
    }

    /// <inheritdoc />
    public async ValueTask MarkProcessedAsync(EnvelopeId id, HandlerName handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(handler);

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "UPDATE cabbie_inbox SET status = 'processed' WHERE id = @id AND handler = @handler AND status = 'in_flight'",
            conn);
        cmd.Parameters.AddWithValue("id", id.Value);
        cmd.Parameters.AddWithValue("handler", handler.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(EnvelopeId id, HandlerName handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(handler);

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "DELETE FROM cabbie_inbox WHERE id = @id AND handler = @handler AND status = 'in_flight'",
            conn);
        cmd.Parameters.AddWithValue("id", id.Value);
        cmd.Parameters.AddWithValue("handler", handler.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<InboxEntry>> LoadInFlightAsync(CancellationToken ct = default)
    {
        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "SELECT id, handler FROM cabbie_inbox WHERE status = 'in_flight' ORDER BY acquired_at",
            conn);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);

        List<InboxEntry> results = new();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new InboxEntry(new EnvelopeId(reader.GetGuid(0)), new HandlerName(reader.GetString(1))));
        }
        return results;
    }
}