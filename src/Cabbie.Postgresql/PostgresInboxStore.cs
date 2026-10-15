using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope.ValueObjects;
using Npgsql;

namespace Cabbie.Postgresql;

/// <summary>
/// Postgres-backed <see cref="IInboxStore"/>. Acquires an incoming envelope id exactly once
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
    public async ValueTask<bool> TryAcquireAsync(EnvelopeId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "INSERT INTO cabbie_inbox (id, status) VALUES (@id, 'in_flight') ON CONFLICT (id) DO NOTHING",
            conn);
        cmd.Parameters.AddWithValue("id", id.Value);
        int inserted = await cmd.ExecuteNonQueryAsync(ct);
        return inserted == 1;
    }

    /// <inheritdoc />
    public async ValueTask MarkProcessedAsync(EnvelopeId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "UPDATE cabbie_inbox SET status = 'processed' WHERE id = @id AND status = 'in_flight'",
            conn);
        cmd.Parameters.AddWithValue("id", id.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(EnvelopeId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "DELETE FROM cabbie_inbox WHERE id = @id AND status = 'in_flight'",
            conn);
        cmd.Parameters.AddWithValue("id", id.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<EnvelopeId>> LoadInFlightAsync(CancellationToken ct = default)
    {
        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "SELECT id FROM cabbie_inbox WHERE status = 'in_flight' ORDER BY acquired_at",
            conn);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);

        List<EnvelopeId> results = new();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new EnvelopeId(reader.GetGuid(0)));
        }
        return results;
    }
}