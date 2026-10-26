using Npgsql;

namespace Cabbie.Postgresql;

/// <summary>
/// Bootstrap helpers for the Postgres adapter. Tables are not auto-migrated on construction;
/// the caller invokes the relevant <c>Ensure*Async</c> method once at startup, typically from
/// the DI registration. Each helper is idempotent (<c>CREATE ... IF NOT EXISTS</c>) so re-running
/// it is safe.
/// </summary>
public static class PostgresSchema
{
    /// <summary>
    /// Create the <c>cabbie_outbox</c> table and its pending-row index if they do not already exist.
    /// <para>
    /// Shape: <c>(id UUID PK, session_id UUID, status TEXT CHECK IN ('staged','pending'),
    /// envelope JSONB, created_at TIMESTAMPTZ DEFAULT now())</c>. The session id isolates staged rows
    /// per stage/commit/rollback cycle; the partial index keeps <c>LoadPendingAsync</c> scans cheap.
    /// </para>
    /// </summary>
    public static async ValueTask EnsureOutboxTableAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        const string sql = """
            CREATE TABLE IF NOT EXISTS cabbie_outbox (
                id UUID PRIMARY KEY,
                session_id UUID NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('staged', 'pending')),
                envelope JSONB NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS cabbie_outbox_pending_idx
                ON cabbie_outbox (created_at)
                WHERE status = 'pending';
            """;

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Create the <c>cabbie_inbox</c> table and its in-flight index if they do not already exist.
    /// <para>
    /// Shape: <c>(id UUID, handler TEXT, PRIMARY KEY (id, handler), status TEXT CHECK IN ('in_flight','processed'),
    /// acquired_at TIMESTAMPTZ DEFAULT now())</c>. The status check gives TryAcquireAsync's
    /// <c>INSERT ... ON CONFLICT DO NOTHING</c> its two outcomes: a fresh in-flight row means
    /// acquired, a conflicting processed or in-flight id means already handled. The partial index
    /// keeps <c>LoadInFlightAsync</c> scans cheap.
    /// </para>
    /// </summary>
    public static async ValueTask EnsureInboxTableAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        const string sql = """
            CREATE TABLE IF NOT EXISTS cabbie_inbox (
                id UUID NOT NULL,
                handler TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('in_flight', 'processed')),
                acquired_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (id, handler)
            );

            -- Upgrade the id-only table an earlier build created: add the handler column and widen the key.
            ALTER TABLE cabbie_inbox ADD COLUMN IF NOT EXISTS handler TEXT NOT NULL DEFAULT '';

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_index i
                    JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey)
                    WHERE i.indrelid = 'cabbie_inbox'::regclass AND i.indisprimary AND a.attname = 'handler')
                THEN
                    ALTER TABLE cabbie_inbox DROP CONSTRAINT cabbie_inbox_pkey;
                    ALTER TABLE cabbie_inbox ADD PRIMARY KEY (id, handler);
                END IF;
            END
            $$;

            CREATE INDEX IF NOT EXISTS cabbie_inbox_in_flight_idx
                ON cabbie_inbox (acquired_at)
                WHERE status = 'in_flight';
            """;

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Create the <c>cabbie_dead_letter</c> table if it does not already exist.
    /// <para>
    /// Shape: <c>(id UUID PK, envelope JSONB, cause_type TEXT, cause_message TEXT,
    /// cause_stack TEXT NULL, recorded_at TIMESTAMPTZ DEFAULT now())</c>. The id is the primary
    /// key so re-recording the same envelope overwrites, matching the in-memory store. The cause
    /// columns preserve what failed; <c>ListAsync</c> reconstructs a best-effort Exception from them.
    /// </para>
    /// </summary>
    public static async ValueTask EnsureDeadLetterTableAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        const string sql = """
            CREATE TABLE IF NOT EXISTS cabbie_dead_letter (
                id UUID PRIMARY KEY,
                envelope JSONB NOT NULL,
                cause_type TEXT NOT NULL,
                cause_message TEXT NOT NULL,
                cause_stack TEXT NULL,
                recorded_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """;

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Create the <c>cabbie_sagas</c> table if it does not already exist.
    /// <para>
    /// Shape: <c>(saga_type TEXT, saga_id TEXT, state JSONB, is_completed BOOLEAN DEFAULT FALSE,
    /// updated_at TIMESTAMPTZ DEFAULT now(), PRIMARY KEY (saga_type, saga_id))</c>. The composite
    /// key mirrors <see cref="ISagaStore"/>'s (sagaType, id) addressing; <c>state</c> is the
    /// serialized saga POCO and <c>is_completed</c> carries the completion flag that is excluded
    /// from the JSON. Last-write-wins upserts, no version column.
    /// </para>
    /// </summary>
    public static async ValueTask EnsureSagaTableAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        const string sql = """
            CREATE TABLE IF NOT EXISTS cabbie_sagas (
                saga_type TEXT NOT NULL,
                saga_id TEXT NOT NULL,
                state JSONB NOT NULL,
                is_completed BOOLEAN NOT NULL DEFAULT FALSE,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (saga_type, saga_id)
            );
            """;

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Create the <c>cabbie_scheduled</c> table and its due-time index if they do not already exist.
    /// <para>
    /// Shape: <c>(seq BIGSERIAL PK, id UUID, due_at TIMESTAMPTZ, headers JSONB, body BYTEA,
    /// locked_until TIMESTAMPTZ NULL, next_attempt_at TIMESTAMPTZ NULL, failures INT DEFAULT 0,
    /// created_at TIMESTAMPTZ DEFAULT now())</c>. The envelope is stored as
    /// its transport form (codec headers plus serialized body). <c>seq</c>, not the envelope id, is the
    /// key and the claim token: a scheduled retry re-parks the same envelope id while the original row is
    /// still claimed. <c>locked_until</c> is the claim lease; a row is claimable when it is null or past.
    /// A failed play sets <c>next_attempt_at</c> (backoff) and bumps <c>failures</c>.
    /// </para>
    /// </summary>
    public static async ValueTask EnsureScheduledTableAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        const string sql = """
            CREATE TABLE IF NOT EXISTS cabbie_scheduled (
                seq BIGSERIAL PRIMARY KEY,
                id UUID NOT NULL,
                due_at TIMESTAMPTZ NOT NULL,
                headers JSONB NOT NULL,
                body BYTEA NOT NULL,
                locked_until TIMESTAMPTZ NULL,
                next_attempt_at TIMESTAMPTZ NULL,
                failures INT NOT NULL DEFAULT 0,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            ALTER TABLE cabbie_scheduled ADD COLUMN IF NOT EXISTS next_attempt_at TIMESTAMPTZ NULL;
            ALTER TABLE cabbie_scheduled ADD COLUMN IF NOT EXISTS failures INT NOT NULL DEFAULT 0;

            DROP INDEX IF EXISTS cabbie_scheduled_due_idx;
            CREATE INDEX IF NOT EXISTS cabbie_scheduled_next_idx
                ON cabbie_scheduled ((COALESCE(next_attempt_at, due_at)), seq);
            """;

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(ct);
        await using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Create every Cabbie Postgres table that the adapter needs. Idempotent;
    /// safe to call repeatedly. Call once at startup before the host starts
    /// processing messages — typically from Program.cs.
    /// </summary>
    public static async ValueTask EnsureAllAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await EnsureOutboxTableAsync(dataSource, ct);
        await EnsureInboxTableAsync(dataSource, ct);
        await EnsureDeadLetterTableAsync(dataSource, ct);
        await EnsureSagaTableAsync(dataSource, ct);
        await EnsureScheduledTableAsync(dataSource, ct);
    }
}