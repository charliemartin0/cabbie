using Cabbie.Application.Persistence;
using Cabbie.Application.Sagas;
using Cabbie.Application.Scheduling;
using Cabbie.Application.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Cabbie.Postgresql;

/// <summary>
/// Host builder extensions that wire the Postgres-backed implementations of the Cabbie
/// persistence, saga and scheduled-envelope ports, and host the poller that plays due scheduled messages. Must be called AFTER <c>UseCabbie</c> — the last-wins
/// registration model means reverse order keeps the in-memory defaults.
/// <para>
/// Schema is not auto-bootstrapped: call <see cref="PostgresSchema.EnsureAllAsync"/>
/// once at startup (typically from Program.cs) before the host starts processing messages;
/// it also creates <c>cabbie_scheduled</c>.
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
        string connectionString,
        PostgresScheduleOptions? schedule = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectionString);

        NpgsqlDataSource dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        return builder.UsePostgres(dataSource, schedule);
    }

    /// <summary>
    /// Wire the Postgres-backed port implementations using an already-built
    /// <see cref="NpgsqlDataSource"/>. The data source is owned by the caller —
    /// its lifetime is the host's lifetime.
    /// </summary>
    public static IHostApplicationBuilder UsePostgres(
        this IHostApplicationBuilder builder,
        NpgsqlDataSource dataSource,
        PostgresScheduleOptions? schedule = null)
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
        builder.Services.AddSingleton<IScheduledEnvelopeHold>(services => new PostgresScheduledEnvelopeHold(
            dataSource,
            services.GetRequiredService<EnvelopeCodec>(),
            schedule,
            services.GetService<TimeProvider>(),
            services.GetService<ILogger<PostgresScheduledEnvelopeHold>>()));
        builder.Services.AddScheduledMessagePoller();

        return builder;
    }
}