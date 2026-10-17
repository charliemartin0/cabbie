namespace Helpdesk.Infrastructure;

using Cabbie.Postgresql;
using Microsoft.Extensions.Hosting;
using Npgsql;

/// <summary>
/// Helpdesk composition root helper: wires the durable Postgres backend.
/// Call AFTER <c>UseCabbie</c>. Reads the connection string from
/// <c>CABBIE_PG_CONNECTIONSTRING</c> at the Program.cs level; the data
/// source is built here so the same instance drives both
/// <c>PostgresSchema.EnsureAllAsync</c> and the Postgres stores.
/// <para>
/// The <see cref="Task{TResult}"/> return is intentional: Program.cs awaits the
/// schema ensure before calling <c>Build()</c>, so the host never starts with
/// missing tables.
/// </para>
/// </summary>
public static class HelpdeskHostBuilderExtensions
{
    public static async Task<IHostApplicationBuilder> UseHelpdeskPostgres(
        this IHostApplicationBuilder builder,
        string connectionString,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectionString);

        NpgsqlDataSource dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        await PostgresSchema.EnsureAllAsync(dataSource, ct);
        builder.UsePostgres(dataSource);
        return builder;
    }
}