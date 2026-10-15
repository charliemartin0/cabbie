using System.Text.Json;
using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Npgsql;

namespace Cabbie.Postgresql;

/// <summary>
/// Postgres-backed <see cref="IOutboxStore"/>. Stages outgoing envelopes in the current
/// "cycle" (identified by an internal session id), commits the cycle to pending, or rolls
/// it back. Pending envelopes survive host restart and are loaded by
/// <see cref="LoadPendingAsync"/> for replay; <see cref="MarkSentAsync"/> removes a pending
/// envelope once Execution takes ownership.
/// <para>
/// Not thread-safe — matches the in-memory store's per-host sequential cycle model.
/// </para>
/// </summary>
public sealed class PostgresOutboxStore : IOutboxStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new EnvelopeDataJsonConverter() },
    };

    private readonly NpgsqlDataSource _dataSource;
    private Guid _sessionId;

    public PostgresOutboxStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
        _sessionId = Guid.NewGuid();
    }

    /// <inheritdoc />
    public IOutboxTransaction BeginOutboxTransaction() =>
        new PostgresOutboxTransaction(_dataSource, JsonOptions);

    /// <inheritdoc />
    public async ValueTask StageAsync(Envelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        string json = JsonSerializer.Serialize(envelope, JsonOptions);

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "INSERT INTO cabbie_outbox (id, session_id, status, envelope) VALUES (@id, @sid, 'staged', @env::jsonb)",
            conn);
        cmd.Parameters.AddWithValue("id", envelope.Id.Value);
        cmd.Parameters.AddWithValue("sid", _sessionId);
        cmd.Parameters.AddWithValue("env", json);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask CommitAsync(CancellationToken ct = default)
    {
        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "UPDATE cabbie_outbox SET status = 'pending' WHERE session_id = @sid AND status = 'staged'",
            conn);
        cmd.Parameters.AddWithValue("sid", _sessionId);
        await cmd.ExecuteNonQueryAsync(ct);
        _sessionId = Guid.NewGuid();
    }

    /// <inheritdoc />
    public async ValueTask RollbackAsync(CancellationToken ct = default)
    {
        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "DELETE FROM cabbie_outbox WHERE session_id = @sid AND status = 'staged'",
            conn);
        cmd.Parameters.AddWithValue("sid", _sessionId);
        await cmd.ExecuteNonQueryAsync(ct);
        _sessionId = Guid.NewGuid();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Envelope>> LoadPendingAsync(CancellationToken ct = default)
    {
        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "SELECT envelope FROM cabbie_outbox WHERE status = 'pending' ORDER BY created_at",
            conn);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);

        List<Envelope> results = new();
        while (await reader.ReadAsync(ct))
        {
            string json = reader.GetString(0);
            Envelope? envelope = JsonSerializer.Deserialize<Envelope>(json, JsonOptions);
            if (envelope is not null)
            {
                results.Add(envelope);
            }
        }
        return results;
    }

    /// <inheritdoc />
    public async ValueTask MarkSentAsync(EnvelopeId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "DELETE FROM cabbie_outbox WHERE id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", id.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
