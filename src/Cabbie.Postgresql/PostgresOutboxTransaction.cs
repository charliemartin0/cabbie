using System.Text.Json;
using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;
using Npgsql;

namespace Cabbie.Postgresql;

/// <summary>
/// Postgres-backed short-lived <see cref="IOutboxTransaction"/>. Staged envelopes are buffered
/// in memory and written as <c>status = 'pending'</c> rows in a single batched INSERT on commit;
/// rollback simply drops the buffer. One instance is single-use — any call after commit or
/// rollback throws <see cref="OutboxTransactionAlreadyCompletedException"/>.
/// <para>
/// This mirrors <see cref="PostgresOutboxStore"/>'s long-lived behaviour (same table, same JSON
/// options) without touching the store's staged rows: transactions from the same store do not
/// share a buffer.
/// </para>
/// </summary>
internal sealed class PostgresOutboxTransaction : IOutboxTransaction
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly List<Envelope> _staged = [];
    private bool _completed;

    public PostgresOutboxTransaction(NpgsqlDataSource dataSource, JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        _dataSource = dataSource;
        _jsonOptions = jsonOptions;
    }

    /// <inheritdoc />
    public ValueTask StageAsync(Envelope envelope, CancellationToken ct = default)
    {
        ThrowIfCompleted();
        ArgumentNullException.ThrowIfNull(envelope);
        _staged.Add(envelope);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask CommitAsync(CancellationToken ct = default)
    {
        ThrowIfCompleted();

        if (_staged.Count > 0)
        {
            await InsertPendingAsync(ct);
        }

        _staged.Clear();
        _completed = true;
    }

    /// <inheritdoc />
    public ValueTask RollbackAsync(CancellationToken ct = default)
    {
        ThrowIfCompleted();
        _staged.Clear();
        _completed = true;
        return ValueTask.CompletedTask;
    }

    private async ValueTask InsertPendingAsync(CancellationToken ct)
    {
        List<string> values = new(_staged.Count);
        for (int i = 0; i < _staged.Count; i++)
        {
            values.Add($"(@id{i}, @sid, 'pending', @env{i}::jsonb)");
        }

        string sql = "INSERT INTO cabbie_outbox (id, session_id, status, envelope) VALUES "
            + string.Join(", ", values);

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("sid", _sessionId);
        for (int i = 0; i < _staged.Count; i++)
        {
            cmd.Parameters.AddWithValue($"id{i}", _staged[i].Id.Value);
            cmd.Parameters.AddWithValue($"env{i}", JsonSerializer.Serialize(_staged[i], _jsonOptions));
        }
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private void ThrowIfCompleted()
    {
        if (_completed)
        {
            throw new OutboxTransactionAlreadyCompletedException();
        }
    }
}
