using System.Collections.Concurrent;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.EntityFrameworkCore;
using Cabbie.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Tests.Postgresql;

internal sealed record OutboxPing(string Value);

internal sealed record OutboxSentinel(Guid Key);

internal sealed class OutboxPingHandler
{
    public static ConcurrentQueue<string> Handled { get; } = new();

    public static ConcurrentQueue<Guid> Sentinels { get; } = new();

    public void Handle(OutboxPing message) => Handled.Enqueue(message.Value);

    public void Handle(OutboxSentinel message) => Sentinels.Enqueue(message.Key);
}

internal sealed class OutboxTestContext(DbContextOptions<OutboxTestContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddCabbieOutbox();
}

/// <summary>Completes once the hosted relay finishes its startup pass and arms its poll timer.</summary>
internal sealed class StartupPassSignal : TimeProvider
{
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task StartupPassDone => _done.Task;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime == TimeSpan.FromDays(1))
        {
            _done.TrySetResult();
        }

        return base.CreateTimer(callback, state, dueTime, period);
    }
}

/// <summary>Sends, then fails once, so the relay dies between the send and the mark.</summary>
internal sealed class CrashAfterSendSender(ISender inner) : ISender
{
    private bool _crashed;

    public async ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        await inner.SendAsync(envelope, cancellationToken);
        if (!_crashed)
        {
            _crashed = true;
            throw new InvalidOperationException("crash");
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Prove-with for the EF outbox: rows commit with the caller's save, the relay hands each to the handler
/// once, and the inbox absorbs a re-send.
/// </summary>
public sealed class EfOutboxTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private static readonly TimeSpan Cap = TimeSpan.FromSeconds(15);

    private readonly PostgresContainerFixture _fixture;

    public EfOutboxTests(PostgresContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using OutboxTestContext context = NewContext();
        // The shared database already holds other tables, so EnsureCreated would no-op.
        string script = context.Database.GenerateCreateScript().Replace(
            "CREATE TABLE", "CREATE TABLE IF NOT EXISTS", StringComparison.Ordinal)
            .Replace("CREATE INDEX", "CREATE INDEX IF NOT EXISTS", StringComparison.Ordinal);
        await context.Database.ExecuteSqlRawAsync(script);
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE cabbie_ef_outbox");
        await _fixture.ResetInboxAsync();
        OutboxPingHandler.Handled.Clear();
        OutboxPingHandler.Sentinels.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task rolled_back_save_writes_no_outbox_rows()
    {
        using IHost host = await StartedHostAsync();
        try
        {
            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<OutboxTestContext>();
                await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
                scope.ServiceProvider.GetRequiredService<EfOutbox<OutboxTestContext>>().Publish(new OutboxPing("a"));
                await context.SaveChangesAsync();
                await transaction.RollbackAsync();
            }

            await using OutboxTestContext check = NewContext();
            Assert.Equal(0, await check.Set<OutboxMessage>().CountAsync());
            Assert.Equal(0, await host.Services.GetRequiredService<OutboxRelay<OutboxTestContext>>().RelayOnceAsync());
            await DrainAsync(host);
            Assert.Empty(OutboxPingHandler.Handled);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task committed_save_relays_exactly_once_to_the_handler()
    {
        using IHost host = await StartedHostAsync();
        try
        {
            await PublishAsync(host, new OutboxPing("a"));
            await using (OutboxTestContext check = NewContext())
            {
                OutboxMessage row = await PingRow(check).SingleAsync();
                Assert.Null(row.SentAt);
            }

            var relay = host.Services.GetRequiredService<OutboxRelay<OutboxTestContext>>();
            Assert.Equal(1, await relay.RelayOnceAsync());
            Assert.Equal(0, await relay.RelayOnceAsync());
            await DrainAsync(host);

            Assert.Equal(["a"], OutboxPingHandler.Handled);
            await using OutboxTestContext after = NewContext();
            Assert.NotNull((await PingRow(after).SingleAsync()).SentAt);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task duplicate_relay_of_the_same_envelope_is_deduped_by_the_inbox()
    {
        using IHost host = await StartedHostAsync();
        try
        {
            await PublishAsync(host, new OutboxPing("a"));
            var relay = host.Services.GetRequiredService<OutboxRelay<OutboxTestContext>>();
            Assert.Equal(1, await relay.RelayOnceAsync());
            await DrainAsync(host);

            await using (OutboxTestContext context = NewContext())
            {
                await PingRow(context).ExecuteUpdateAsync(
                    set => set.SetProperty(message => message.SentAt, (DateTimeOffset?)null));
            }

            Assert.Equal(1, await relay.RelayOnceAsync());
            await DrainAsync(host);

            Assert.Equal(["a"], OutboxPingHandler.Handled);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task relay_crash_between_send_and_mark_does_not_double_handle()
    {
        using IHost host = await StartedHostAsync();
        try
        {
            await PublishAsync(host, new OutboxPing("a"));
            var crashing = new OutboxRelay<OutboxTestContext>(
                host.Services.GetRequiredService<IServiceScopeFactory>(),
                new CrashAfterSendSender(host.Services.GetRequiredService<TransportRouter>()),
                host.Services.GetRequiredService<EnvelopeCodec>(),
                TimeProvider.System,
                new EfOutboxRelayOptions());

            await Assert.ThrowsAsync<InvalidOperationException>(() => crashing.RelayOnceAsync());
            await using (OutboxTestContext check = NewContext())
            {
                Assert.Null((await PingRow(check).SingleAsync()).SentAt);
            }

            var relay = host.Services.GetRequiredService<OutboxRelay<OutboxTestContext>>();
            Assert.Equal(1, await relay.RelayOnceAsync());
            await DrainAsync(host);

            Assert.Equal(["a"], OutboxPingHandler.Handled);
            await using OutboxTestContext after = NewContext();
            Assert.NotNull((await PingRow(after).SingleAsync()).SentAt);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task unreadable_rows_beyond_the_batch_size_are_dead_lettered_and_a_later_good_row_still_relays()
    {
        using IHost host = await StartedHostAsync(batchSize: 3);
        try
        {
            await using (OutboxTestContext seed = NewContext())
            {
                for (int i = 0; i < 4; i++)
                {
                    seed.Set<OutboxMessage>().Add(new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        Destination = "outbox-test",
                        MessageType = "No.Such.Type",
                        Headers = "not json",
                        Body = [],
                        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10 + i),
                    });
                }

                await seed.SaveChangesAsync();
            }

            await PublishAsync(host, new OutboxPing("good"));
            var relay = host.Services.GetRequiredService<OutboxRelay<OutboxTestContext>>();
            int sent = 0;
            for (int pass = 0; pass < 3 && sent == 0; pass++)
            {
                sent += await relay.RelayOnceAsync();
            }

            Assert.Equal(1, sent);
            await DrainAsync(host);
            Assert.Equal(["good"], OutboxPingHandler.Handled);
            await using OutboxTestContext after = NewContext();
            List<OutboxMessage> bad = await after.Set<OutboxMessage>()
                .Where(message => message.MessageType == "No.Such.Type").ToListAsync();
            Assert.Equal(4, bad.Count);
            Assert.All(bad, row =>
            {
                Assert.NotNull(row.DeadLetteredAt);
                Assert.Null(row.SentAt);
            });
            Assert.Equal(0, await relay.RelayOnceAsync());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static IQueryable<OutboxMessage> PingRow(OutboxTestContext context)
    {
        string type = typeof(OutboxPing).FullName!;
        return context.Set<OutboxMessage>().Where(message => message.MessageType == type);
    }

    private OutboxTestContext NewContext() => new(
        new DbContextOptionsBuilder<OutboxTestContext>().UseNpgsql(_fixture.ConnectionString).Options);

    private async Task<IHost> StartedHostAsync(int batchSize = 50)
    {
        var signal = new StartupPassSignal();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<TimeProvider>(signal);
        builder.UseCabbie(options =>
        {
            options.HandlerAssemblies.Add(typeof(OutboxPingHandler).Assembly);
            options.PublishMessage<OutboxPing>().ToLocalQueue("outbox-test");
            options.PublishMessage<OutboxSentinel>().ToLocalQueue("outbox-test");
        });
        builder.UsePostgres(_fixture.ConnectionString);
        builder.Services.AddDbContext<OutboxTestContext>(
            options => options.UseNpgsql(_fixture.ConnectionString));
        // A day-long interval keeps the hosted relay from racing the passes the tests drive by hand.
        builder.Services.AddCabbieEfOutbox<OutboxTestContext>(
            new EfOutboxRelayOptions { PollInterval = TimeSpan.FromDays(1), BatchSize = batchSize });
        IHost host = builder.Build();
        await host.StartAsync();
        // The hosted relay arms its inter-pass timer only after the startup pass; wait for that so it cannot take a test's row.
        await signal.StartupPassDone.WaitAsync(Cap);
        return host;
    }

    private static async Task PublishAsync(IHost host, object message)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EfOutbox<OutboxTestContext>>().Publish(message);
        await scope.ServiceProvider.GetRequiredService<OutboxTestContext>().SaveChangesAsync();
    }

    /// <summary>
    /// Relays a sentinel behind everything already sent and waits for it. The queue is one FIFO reader,
    /// so once the sentinel is handled every earlier delivery has been handled too.
    /// </summary>
    private static async Task DrainAsync(IHost host)
    {
        var key = Guid.NewGuid();
        await PublishAsync(host, new OutboxSentinel(key));
        await host.Services.GetRequiredService<OutboxRelay<OutboxTestContext>>().RelayOnceAsync();
        DateTime deadline = DateTime.UtcNow + Cap;
        while (!OutboxPingHandler.Sentinels.Contains(key))
        {
            Assert.True(DateTime.UtcNow < deadline, "sentinel was not handled in time");
            await Task.Delay(20);
        }
    }
}
