using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Cabbie.Application.Sagas;
using Cabbie.Domain.Sagas;
using Cabbie.Domain.Sagas.ValueObjects;
using Npgsql;

namespace Cabbie.Postgresql;

/// <summary>
/// Postgres-backed <see cref="ISagaStore"/>. Snapshots saga state into the <c>cabbie_sagas</c>
/// table keyed by <c>(saga_type, saga_id)</c>; saves upsert (last-write-wins, no versioning) and
/// each load deserializes a fresh instance so mutations never leak into the stored row.
/// <para>
/// One connection per operation, mirroring <see cref="PostgresOutboxStore"/>. The sync port is
/// served with sync Npgsql calls.
/// </para>
/// </summary>
public sealed class PostgresSagaStore : ISagaStore
{
    // IsCompleted has a private setter, so default STJ would serialize it (public getter) but
    // could not restore it on deserialize. Instead it is excluded from the state JSON and carried
    // by the dedicated is_completed column; Load restores it via the public MarkCompleted().
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { ExcludeSagaIsCompleted },
        },
    };

    private readonly NpgsqlDataSource _dataSource;

    public PostgresSagaStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    /// <inheritdoc />
    public Saga? Load(Type sagaType, SagaId id)
    {
        ArgumentNullException.ThrowIfNull(sagaType);
        ArgumentNullException.ThrowIfNull(id);

        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using NpgsqlCommand cmd = new NpgsqlCommand(
            "SELECT state, is_completed FROM cabbie_sagas WHERE saga_type = @type AND saga_id = @id",
            conn);
        cmd.Parameters.AddWithValue("type", sagaType.AssemblyQualifiedName!);
        cmd.Parameters.AddWithValue("id", id.Value);
        using NpgsqlDataReader reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        string stateJson = reader.GetString(0);
        bool isCompleted = reader.GetBoolean(1);

        object? deserialized;
        try
        {
            deserialized = JsonSerializer.Deserialize(stateJson, sagaType, JsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize saga state for {sagaType.AssemblyQualifiedName} (id '{id.Value}').",
                ex);
        }

        if (deserialized is not Saga saga)
        {
            throw new InvalidOperationException(
                $"Deserialized state for {sagaType.AssemblyQualifiedName} (id '{id.Value}') was not a Saga instance.");
        }

        if (isCompleted)
        {
            saga.MarkCompleted();
        }

        return saga;
    }

    /// <inheritdoc />
    public void Save(Type sagaType, SagaId id, Saga instance)
    {
        ArgumentNullException.ThrowIfNull(sagaType);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(instance);

        string stateJson = JsonSerializer.Serialize(instance, sagaType, JsonOptions);

        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using NpgsqlCommand cmd = new NpgsqlCommand(
            """
            INSERT INTO cabbie_sagas (saga_type, saga_id, state, is_completed)
            VALUES (@type, @id, @state::jsonb, @completed)
            ON CONFLICT (saga_type, saga_id)
            DO UPDATE SET state = EXCLUDED.state, is_completed = EXCLUDED.is_completed, updated_at = now()
            """,
            conn);
        cmd.Parameters.AddWithValue("type", sagaType.AssemblyQualifiedName!);
        cmd.Parameters.AddWithValue("id", id.Value);
        cmd.Parameters.AddWithValue("state", stateJson);
        cmd.Parameters.AddWithValue("completed", instance.IsCompleted);
        cmd.ExecuteNonQuery();
    }

    private static void ExcludeSagaIsCompleted(JsonTypeInfo typeInfo)
    {
        if (typeof(Saga).IsAssignableFrom(typeInfo.Type))
        {
            for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
            {
                if (typeInfo.Properties[i].Name == nameof(Saga.IsCompleted))
                {
                    typeInfo.Properties.RemoveAt(i);
                }
            }
        }
    }
}