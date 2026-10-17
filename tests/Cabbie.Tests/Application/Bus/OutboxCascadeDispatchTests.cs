using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Persistence;
using Cabbie.Application.Sagas;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Sagas;
using Cabbie.Domain.Sagas.ValueObjects;

namespace Cabbie.Tests.Application.Bus;

/// <summary>
/// Prove-with for the transactional outbox end-to-end in-memory: with the flag on, a
/// handler's cascades are staged on the handler's outbox transaction and committed as
/// pending (replayable on host start); a throwing handler stages nothing; without the
/// flag the immediate publish path is untouched.
/// </summary>
public sealed class OutboxCascadeDispatchTests
{
    [Fact]
    public async Task outbox_handler_cascade_is_staged_in_pending_after_handler_success()
    {
        using IHost host = await BuildHostAsync(useOutbox: true, [typeof(OutboxHandlerA)]);

        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(new OutboxTrigger(1));

        Envelope pending = Assert.Single(await PendingAsync(host));
        Assert.IsType<OutboxCascade>(pending.Message.Value);
    }

    [Fact]
    public async Task outbox_handler_cascade_is_not_in_pending_after_handler_throws()
    {
        using IHost host = await BuildHostAsync(useOutbox: true, [typeof(OutboxThrowingHandler)]);

        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
        await Assert.ThrowsAsync<HandlerFault>(() => bus.InvokeAsync(new OutboxTrigger(1)));

        Assert.Empty(await PendingAsync(host));
    }

    [Fact]
    public async Task outbox_saga_cascade_is_staged_in_pending_after_handle_success()
    {
        using IHost host = await BuildHostAsync(useOutbox: true, [typeof(OutboxSaga)]);

        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(new OutboxSagaStart(1));

        Envelope pending = Assert.Single(await PendingAsync(host));
        Assert.IsType<OutboxCascade>(pending.Message.Value);
    }

    [Fact]
    public async Task outbox_handler_cascade_without_transactional_flag_uses_immediate_publish_path()
    {
        using IHost host = await BuildHostAsync(useOutbox: false, [typeof(OutboxHandlerA)]);

        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(new OutboxTrigger(1));

        Assert.Empty(await PendingAsync(host));
    }

    [Fact]
    public async Task outbox_saga_save_failure_leaves_no_pending_cascades()
    {
        using IHost host = await BuildHostAsync(
            useOutbox: true,
            [typeof(OutboxSaga)],
            new FailingSaveSagaStore());

        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
        await Assert.ThrowsAsync<TimeoutException>(() => bus.InvokeAsync(new OutboxSagaStart(1)));

        // The commit happens AFTER _sagas.Save; a save throw rolls back, so nothing is
        // left pending — "no save then publish".
        Assert.Empty(await PendingAsync(host));
    }

    private static async Task<IReadOnlyList<Envelope>> PendingAsync(IHost host)
    {
        IMessageStore store = host.Services.GetRequiredService<IMessageStore>();
        return await store.Outbox.LoadPendingAsync();
    }

    private static async Task<IHost> BuildHostAsync(
        bool useOutbox,
        Type[] handlerTypes,
        ISagaStore? sagaStore = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options =>
        {
            if (useOutbox)
            {
                options.UseTransactionalOutbox();
            }
        });

        if (sagaStore is not null)
        {
            // Last registration wins for ISagaStore — override the in-memory default.
            builder.Services.AddSingleton<ISagaStore>(sagaStore);
        }

        IHost host = builder.Build();
        HandlerCatalog catalog = host.Services.GetRequiredService<HandlerCatalog>();
        foreach (Type handlerType in handlerTypes)
        {
            catalog.Scan(handlerType);
        }

        await host.StartAsync();
        return host;
    }
}

public sealed record OutboxTrigger(int Id);

public sealed record OutboxCascade(int Id);

public sealed class OutboxHandlerA
{
    public OutboxCascade Handle(OutboxTrigger trigger) => new(trigger.Id);
}

public sealed class OutboxThrowingHandler
{
    public OutboxCascade Handle(OutboxTrigger trigger) => throw new TimeoutException("boom");
}

public sealed record OutboxSagaStart([property: SagaIdentity] int Id);

public sealed class OutboxSaga : Saga
{
    public OutboxCascade Start(OutboxSagaStart message) => new(message.Id);
}

/// <summary>
/// Saga store that never loads a row (first message is always a start) and throws on
/// every save — pins the "no save then publish" contract: a failed saga save must roll
/// back the outbox transaction instead of leaving the staged cascades pending.
/// </summary>
public sealed class FailingSaveSagaStore : ISagaStore
{
    public Saga? Load(Type sagaType, SagaId id) => null;

    public void Save(Type sagaType, SagaId id, Saga instance) =>
        throw new TimeoutException("save failed");
}