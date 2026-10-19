using System.Collections.Concurrent;
using Cabbie.Application.Bus;
using Cabbie.Application.Scheduling;
using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Infrastructure.Serialization;
using Cabbie.Postgresql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Cabbie.Tests.Postgresql;

internal sealed record ScheduledPing(Guid Key);

internal sealed record ScheduledUnknown(Guid Key);

internal sealed class ScheduledPingHandler
{
    public static ConcurrentQueue<Guid> Handled { get; } = new();

    public void Handle(ScheduledPing message) => Handled.Enqueue(message.Key);
}

/// <summary>
/// Prove-with for the durable scheduler: due messages are claimed once, survive a host restart, and
/// two pollers never receive the same message.
/// </summary>
public sealed class PostgresScheduledEnvelopeHoldTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private static readonly TimeSpan Cap = TimeSpan.FromSeconds(15);

    private readonly PostgresContainerFixture _fixture;

    public PostgresScheduledEnvelopeHoldTests(PostgresContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetScheduledAsync();
        ScheduledPingHandler.Handled.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task scheduled_message_is_delivered_after_due_time()
    {
        PostgresScheduledEnvelopeHold hold = NewHold();
        DateTimeOffset due = DateTimeOffset.UtcNow.AddMinutes(1);
        Envelope parked = NewEnvelope(new ScheduledPing(Guid.NewGuid()), due, tenant: "t1", attempts: 3);
        await hold.ParkAsync(parked);

        Assert.Empty(await hold.ClaimDueAsync(due.AddSeconds(-1)));

        ClaimedEnvelope claim = Assert.Single(await hold.ClaimDueAsync(due));
        Assert.Equal(parked.Id, claim.Envelope.Id);
        Assert.Equal(parked.Message.Value, claim.Envelope.Message.Value);
        Assert.Equal(parked.Attempts, claim.Envelope.Attempts);
        Assert.Equal(due, claim.Envelope.DeliverBy.Value);
        Assert.Equal("t1", claim.Envelope.Headers.Value["tenant"]);

        await hold.CompleteAsync(claim);

        Assert.Equal(0, await CountRowsAsync());
    }

    [Fact]
    public async Task scheduled_message_survives_host_restart()
    {
        var clock = new ManualClock();
        var key = Guid.NewGuid();

        using (IHost first = await StartedHostAsync(clock))
        {
            await first.Services.GetRequiredService<IMessageBus>()
                .PublishAsync(new ScheduledPing(key), new DeliveryOptions { Delay = TimeSpan.FromMinutes(1) });
            await first.StopAsync();
        }

        Assert.Equal(1, await CountRowsAsync());

        using IHost second = await StartedHostAsync(clock);
        try
        {
            Assert.Empty(ScheduledPingHandler.Handled);

            await AdvanceUntilAsync(clock, () => Task.FromResult(ScheduledPingHandler.Handled.Contains(key)));

            Assert.Equal([key], ScheduledPingHandler.Handled);
            await AdvanceUntilAsync(clock, async () => await CountRowsAsync() == 0);
        }
        finally
        {
            await second.StopAsync();
        }
    }

    [Fact]
    public async Task deferred_row_is_claimable_only_after_next_attempt_and_keeps_its_failure_count()
    {
        PostgresScheduledEnvelopeHold hold = NewHold();
        DateTimeOffset due = DateTimeOffset.UtcNow.AddMinutes(-10);
        Envelope poison = NewEnvelope(new ScheduledPing(Guid.NewGuid()), due);
        Envelope good = NewEnvelope(new ScheduledPing(Guid.NewGuid()), due.AddMinutes(1));
        await hold.ParkAsync(poison);
        await hold.ParkAsync(good);
        IReadOnlyList<ClaimedEnvelope> claims = await hold.ClaimDueAsync(DateTimeOffset.UtcNow);
        Assert.Equal([poison.Id, good.Id], claims.Select(c => c.Envelope.Id));
        DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(30);

        await hold.DeferAsync(claims[0], notBefore);
        await hold.CompleteAsync(claims[1]);

        Assert.Empty(await hold.ClaimDueAsync(notBefore.AddSeconds(-1)));
        ClaimedEnvelope again = Assert.Single(await hold.ClaimDueAsync(notBefore));
        Assert.Equal(poison.Id, again.Envelope.Id);
        Assert.Equal(1, again.Failures);
    }

    [Fact]
    public async Task ensuring_the_schema_upgrades_a_table_created_before_backoff_columns()
    {
        await using (NpgsqlConnection conn = await _fixture.DataSource.OpenConnectionAsync())
        await using (NpgsqlCommand cmd = new NpgsqlCommand(
            """
            DROP TABLE cabbie_scheduled;
            CREATE TABLE cabbie_scheduled (
                seq BIGSERIAL PRIMARY KEY, id UUID NOT NULL, due_at TIMESTAMPTZ NOT NULL,
                headers JSONB NOT NULL, body BYTEA NOT NULL, locked_until TIMESTAMPTZ NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now());
            CREATE INDEX cabbie_scheduled_due_idx ON cabbie_scheduled (due_at, seq);
            """,
            conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await PostgresSchema.EnsureScheduledTableAsync(_fixture.DataSource);

        PostgresScheduledEnvelopeHold hold = NewHold();
        DateTimeOffset due = DateTimeOffset.UtcNow.AddMinutes(-1);
        await hold.ParkAsync(NewEnvelope(new ScheduledPing(Guid.NewGuid()), due));
        ClaimedEnvelope claim = Assert.Single(await hold.ClaimDueAsync(DateTimeOffset.UtcNow));
        await hold.DeferAsync(claim, DateTimeOffset.UtcNow);
        Assert.Equal(1, Assert.Single(await hold.ClaimDueAsync(DateTimeOffset.UtcNow.AddSeconds(1))).Failures);
    }

    [Fact]
    public async Task two_pollers_never_deliver_the_same_message_twice()
    {
        DateTimeOffset due = DateTimeOffset.UtcNow.AddMinutes(-1);
        PostgresScheduledEnvelopeHold seed = NewHold();
        List<EnvelopeId> expected = [];
        for (int i = 0; i < 50; i++)
        {
            Envelope envelope = NewEnvelope(new ScheduledPing(Guid.NewGuid()), due);
            expected.Add(envelope.Id);
            await seed.ParkAsync(envelope);
        }

        await using NpgsqlDataSource otherSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).Build();
        var options = new PostgresScheduleOptions { BatchSize = 7 };
        var pollers = new[] { NewHold(options: options), NewHold(otherSource, options) };

        List<EnvelopeId>[] delivered = await Task.WhenAll(pollers.Select(DrainAsync));

        List<EnvelopeId> all = [.. delivered.SelectMany(ids => ids)];
        Assert.Equal(expected.Count, all.Count);
        Assert.Equal(expected.OrderBy(id => id.Value), all.OrderBy(id => id.Value));
        Assert.Equal(0, await CountRowsAsync());

        async Task<List<EnvelopeId>> DrainAsync(PostgresScheduledEnvelopeHold poller)
        {
            List<EnvelopeId> ids = [];
            while (await poller.ClaimDueAsync(DateTimeOffset.UtcNow) is { Count: > 0 } batch)
            {
                foreach (ClaimedEnvelope claim in batch)
                {
                    ids.Add(claim.Envelope.Id);
                    await poller.CompleteAsync(claim);
                }
            }

            return ids;
        }
    }

    [Fact]
    public async Task claimed_but_uncompleted_message_is_redelivered_after_the_lease_expires()
    {
        var clock = new ManualClock();
        PostgresScheduledEnvelopeHold hold = NewHold(
            options: new PostgresScheduleOptions { ClaimLease = TimeSpan.FromMinutes(1) },
            clock: clock);
        DateTimeOffset due = clock.GetUtcNow().AddMinutes(-1);
        Envelope envelope = NewEnvelope(new ScheduledPing(Guid.NewGuid()), due);
        await hold.ParkAsync(envelope);
        Assert.Single(await hold.ClaimDueAsync(due));

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Empty(await hold.ClaimDueAsync(due));

        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(envelope.Id, Assert.Single(await hold.ClaimDueAsync(due)).Envelope.Id);
    }

    [Fact]
    public async Task released_message_is_claimable_immediately()
    {
        PostgresScheduledEnvelopeHold hold = NewHold();
        DateTimeOffset due = DateTimeOffset.UtcNow.AddMinutes(-1);
        await hold.ParkAsync(NewEnvelope(new ScheduledPing(Guid.NewGuid()), due));
        ClaimedEnvelope claim = Assert.Single(await hold.ClaimDueAsync(due));
        Assert.Empty(await hold.ClaimDueAsync(due));

        await hold.ReleaseAsync(claim);

        Assert.Single(await hold.ClaimDueAsync(due));
    }

    [Fact]
    public async Task completing_one_claim_leaves_a_repark_of_the_same_envelope_id_in_place()
    {
        PostgresScheduledEnvelopeHold hold = NewHold();
        DateTimeOffset due = DateTimeOffset.UtcNow.AddMinutes(-1);
        Envelope envelope = NewEnvelope(new ScheduledPing(Guid.NewGuid()), due);
        await hold.ParkAsync(envelope);
        ClaimedEnvelope claim = Assert.Single(await hold.ClaimDueAsync(due));
        await hold.ParkAsync(envelope with { Attempts = new Attempts(2) });

        await hold.CompleteAsync(claim);

        ClaimedEnvelope retry = Assert.Single(await hold.ClaimDueAsync(due));
        Assert.Equal(2, retry.Envelope.Attempts.Value);
    }

    [Fact]
    public async Task undecodable_row_is_skipped_and_does_not_block_later_rows()
    {
        DateTimeOffset due = DateTimeOffset.UtcNow.AddMinutes(-1);
        PostgresScheduledEnvelopeHold writer = NewHold(types: [typeof(ScheduledPing), typeof(ScheduledUnknown)]);
        await writer.ParkAsync(NewEnvelope(new ScheduledUnknown(Guid.NewGuid()), due.AddSeconds(-5)));
        Envelope known = NewEnvelope(new ScheduledPing(Guid.NewGuid()), due);
        await writer.ParkAsync(known);
        PostgresScheduledEnvelopeHold reader = NewHold(types: [typeof(ScheduledPing)]);

        ClaimedEnvelope claim = Assert.Single(await reader.ClaimDueAsync(due));

        Assert.Equal(known.Id, claim.Envelope.Id);
        Assert.Equal(2, await CountRowsAsync());
    }

    [Fact]
    public async Task parking_an_envelope_without_a_delivery_time_is_rejected()
    {
        PostgresScheduledEnvelopeHold hold = NewHold();
        Envelope envelope = NewEnvelope(new ScheduledPing(Guid.NewGuid()), DateTimeOffset.UtcNow) with
        {
            DeliverBy = new DeliverBy(null),
        };

        await Assert.ThrowsAsync<ArgumentException>(() => hold.ParkAsync(envelope).AsTask());
    }

    private PostgresScheduledEnvelopeHold NewHold(
        NpgsqlDataSource? source = null,
        PostgresScheduleOptions? options = null,
        TimeProvider? clock = null,
        Type[]? types = null)
    {
        var catalog = new MessageTypeCatalog();
        foreach (Type type in types ?? [typeof(ScheduledPing)])
        {
            catalog.Register(type);
        }

        return new PostgresScheduledEnvelopeHold(
            source ?? _fixture.DataSource,
            new EnvelopeCodec(new JsonMessageSerializer(), catalog),
            options,
            clock);
    }

    private static Envelope NewEnvelope(object message, DateTimeOffset due, string? tenant = null, int attempts = 1) =>
        new(
            new EnvelopeId(Guid.NewGuid()),
            new Message(message),
            MessageTypeNaming.For(message.GetType()),
            new Destination(new Uri("local://scheduled/")),
            new CorrelationId(Guid.NewGuid()),
            new ConversationId(Guid.NewGuid()),
            new SagaId(""),
            new SentAt(due.AddMinutes(-1)),
            new DeliverBy(due),
            new Headers(tenant is null ? [] : new Dictionary<string, string> { ["tenant"] = tenant }),
            new ContentType(""),
            new Attempts(attempts),
            new EnvelopeData());

    private async Task<IHost> StartedHostAsync(TimeProvider clock)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(clock);
        builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(ScheduledPingHandler).Assembly));
        builder.UsePostgres(_fixture.ConnectionString);
        IHost host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static async Task AdvanceUntilAsync(ManualClock clock, Func<Task<bool>> condition)
    {
        using var cap = new CancellationTokenSource(Cap);
        while (!await condition())
        {
            // The poller may not have armed its delay yet; keep moving the clock until a tick lands.
            clock.Advance(TimeSpan.FromMinutes(2));
            await Task.Delay(TimeSpan.FromMilliseconds(50), cap.Token);
        }
    }

    private async Task<long> CountRowsAsync()
    {
        await using NpgsqlConnection conn = await _fixture.DataSource.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new NpgsqlCommand("SELECT count(*) FROM cabbie_scheduled", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
