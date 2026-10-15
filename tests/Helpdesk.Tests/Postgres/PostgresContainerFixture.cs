using Cabbie.Postgresql;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Helpdesk.Tests.Postgres;

/// <summary>
/// Shared Postgres container for the Helpdesk conversation facts. Starts once per
/// class, bootstraps every Cabbie table via <c>PostgresSchema.EnsureAllAsync</c>, and
/// exposes TRUNCATE helpers so each fact gets a fresh set of tables.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    static PostgresContainerFixture()
    {
        // Linux Docker Desktop stores its socket under ~/.docker/desktop/ instead of
        // the default /var/run/docker.sock. If DOCKER_HOST is unset and the Desktop
        // socket is reachable, point Testcontainers at it so `dotnet test` works
        // without extra env setup on those hosts. CI / standard Linux setups keep
        // their own DOCKER_HOST and are unaffected.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            string desktopSocket = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".docker",
                "desktop",
                "docker.sock");
            if (Path.Exists(desktopSocket))
            {
                Environment.SetEnvironmentVariable("DOCKER_HOST", $"unix://{desktopSocket}");
            }
        }
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        await PostgresSchema.EnsureAllAsync(DataSource);
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    public async Task ResetOutboxAsync()
    {
        await using NpgsqlConnection conn = await DataSource.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new NpgsqlCommand("TRUNCATE TABLE cabbie_outbox", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ResetInboxAsync()
    {
        await using NpgsqlConnection conn = await DataSource.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new NpgsqlCommand("TRUNCATE TABLE cabbie_inbox", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ResetDeadLetterAsync()
    {
        await using NpgsqlConnection conn = await DataSource.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new NpgsqlCommand("TRUNCATE TABLE cabbie_dead_letter", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ResetSagasAsync()
    {
        await using NpgsqlConnection conn = await DataSource.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new NpgsqlCommand("TRUNCATE TABLE cabbie_sagas", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ResetAllAsync()
    {
        await ResetOutboxAsync();
        await ResetInboxAsync();
        await ResetDeadLetterAsync();
        await ResetSagasAsync();
    }
}