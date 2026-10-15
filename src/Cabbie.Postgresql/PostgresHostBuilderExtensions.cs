using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Cabbie.Application.Persistence;
using Cabbie.Application.Sagas;
using Npgsql;

namespace Cabbie.Postgresql;

/// <summary>
/// Host builder extensions that wire the Postgres-backed implementations of the Cabbie
/// persistence and saga ports. Must be called AFTER <c>UseCabbie</c> — the last-wins
/// registration model means reverse order keeps the in-memory defaults.
/// <para>
/// Schema is not auto-bootstrapped: call <see cref="PostgresSchema.EnsureAllAsync"/>
/// once at startup (typically from Program.cs) before the host starts processing messages.
/// </para>
/// </summary>
public static class PostgresHostBuilderExtensions
{
    /// <summary>
    /// Wire the Postgres-backed port implementations using a connection string. The adapter
    /// builds and owns the <see cref="NpgsqlDataSource"/>; its lifetime is the host's lifetime.
    /// </summary>
    public static IHostApplicationBuilder UsePostgres(
        this IHostApplicationBuilder builder,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectionString);

        NpgsqlDataSource dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        return builder.UsePostgres(dataSource);
    }

    /// <summary>
    /// Wire the Postgres-backed port implementations using an already-built
    /// <see cref="NpgsqlDataSource"/>. The data source is owned by the caller —
    /// its lifetime is the host's lifetime.
    /// </summary>
    public static IHostApplicationBuilder UsePostgres(
        this IHostApplicationBuilder builder,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(dataSource);

        // Construct sub-stores directly. The kernel only consumes IMessageStore (composed)
        // and ISagaStore today; the individual outbox/inbox/dead-letter ports are not
        // registered separately — the composite is the seam, and replacing IMessageStore
        // keeps the kernel's IDeadLetterStore/IErrorQueue factories consistent.
        var outbox = new PostgresOutboxStore(dataSource);
        var inbox = new PostgresInboxStore(dataSource);
        var deadLetter = new PostgresDeadLetterStore(dataSource);
        var saga = new PostgresSagaStore(dataSource);

        builder.Services.AddSingleton<ISagaStore>(saga);
        builder.Services.AddSingleton<IMessageStore>(_ =>
            new CompositeMessageStore(outbox, inbox, deadLetter));

        return builder;
    }
}