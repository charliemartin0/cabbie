using System.Text.Json;
using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;
using Npgsql;

namespace Cabbie.Postgresql;

/// <summary>
/// Postgres-backed <see cref="IDeadLetterStore"/>. Records envelopes that exhausted their
/// retries together with the cause, and lists them for inspection. The id is the primary key,
/// so re-recording the same envelope overwrites, matching the in-memory store.
/// <para>
/// One connection per operation, mirroring <see cref="PostgresOutboxStore"/>.
/// </para>
/// </summary>
public sealed class PostgresDeadLetterStore : IDeadLetterStore
{
    // Mirror of PostgresOutboxStore.JsonOptions — extract a shared internal options type in a
    // follow-up so both stores reference the same instance instead of copying it.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new EnvelopeDataJsonConverter() },
    };

    private readonly NpgsqlDataSource _dataSource;

    public PostgresDeadLetterStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    /// <inheritdoc />
    public async ValueTask RecordAsync(Envelope envelope, Exception cause, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(cause);

        string json = JsonSerializer.Serialize(envelope, JsonOptions);

        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "INSERT INTO cabbie_dead_letter (id, envelope, cause_type, cause_message, cause_stack) VALUES (@id, @env::jsonb, @ctype, @cmsg, @cstack)",
            conn);
        cmd.Parameters.AddWithValue("id", envelope.Id.Value);
        cmd.Parameters.AddWithValue("env", json);
        cmd.Parameters.AddWithValue("ctype", cause.GetType().FullName ?? cause.GetType().Name);
        cmd.Parameters.AddWithValue("cmsg", cause.Message);
        cmd.Parameters.AddWithValue("cstack", cause.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DeadLetter>> ListAsync(CancellationToken ct = default)
    {
        await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "SELECT envelope, cause_type, cause_message, cause_stack, recorded_at FROM cabbie_dead_letter ORDER BY recorded_at",
            conn);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);

        List<DeadLetter> results = new();
        while (await reader.ReadAsync(ct))
        {
            string json = reader.GetString(0);
            Envelope? envelope = JsonSerializer.Deserialize<Envelope>(json, JsonOptions);
            if (envelope is null)
            {
                continue;
            }

            results.Add(new DeadLetter(
                envelope,
                ReconstructCause(reader.GetString(1), reader.GetString(2)),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }
        return results;
    }

    /// <summary>
    /// Rebuild a best-effort exception from the stored cause columns. Only the type and message
    /// can round-trip; the original stack is preserved verbatim in <c>cause_stack</c> for
    /// inspection but is not rehydrated into the object.
    /// </summary>
    private static Exception ReconstructCause(string causeType, string causeMessage) => causeType switch
    {
        "System.InvalidOperationException" => new InvalidOperationException(causeMessage),
        "System.ArgumentException" => new ArgumentException(causeMessage),
        "System.TimeoutException" => new TimeoutException(causeMessage),
        _ => new Exception(causeMessage),
    };
}