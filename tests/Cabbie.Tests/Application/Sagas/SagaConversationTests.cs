using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Sagas;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Errors.ValueObjects;
using Cabbie.Domain.Sagas;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Infrastructure.Sagas;
using Cabbie.Tests.Application.Mediator;

namespace Cabbie.Tests.Application.Sagas;

public sealed class SagaConversationTests
{
    public SagaConversationTests()
    {
        ConversationSaga.TimeoutHandled = false;
        ConversationSaga.TimeoutNotFound = false;
        ConversationSaga.LastEnvelopeSagaId = "";
        ConversationObserver.LastSagaId = "";
    }

    [Fact]
    public async Task payment_charged_loads_the_same_instance_start_created()
    {
        var store = new InMemorySagaStore();
        IMessageBus bus = Bus(store);

        await bus.InvokeAsync(new ConversationStarted(1));
        await bus.InvokeAsync(new ConversationPaid(1));

        var saga = Assert.IsType<ConversationSaga>(store.Load(typeof(ConversationSaga), new SagaId("1")));
        Assert.Equal("paid-started", saga.Token);
        Assert.True(saga.IsCompleted);
    }

    [Fact]
    public async Task after_complete_timeout_hits_not_found_instead_of_failing()
    {
        var store = new InMemorySagaStore();
        IMessageBus bus = Bus(store);

        await bus.InvokeAsync(new ConversationStarted(1));
        await bus.InvokeAsync(new ConversationPaid(1));
        await bus.InvokeAsync(new ConversationTimeout(1));

        Assert.False(ConversationSaga.TimeoutHandled);
        Assert.True(ConversationSaga.TimeoutNotFound);
    }

    [Fact]
    public async Task never_started_timeout_with_not_found_is_the_same_miss_path()
    {
        IMessageBus bus = Bus(new InMemorySagaStore());

        await bus.InvokeAsync(new ConversationTimeout(1));

        Assert.False(ConversationSaga.TimeoutHandled);
        Assert.True(ConversationSaga.TimeoutNotFound);
    }

    [Fact]
    public async Task miss_without_not_found_throws_saga_instance_not_found()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(PaidOnlySaga));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, sagas: new InMemorySagaStore());

        SagaInstanceNotFound error = await Assert.ThrowsAsync<SagaInstanceNotFound>(
            () => bus.InvokeAsync(new ConversationPaid(1)));

        Assert.Equal(typeof(PaidOnlySaga), error.SagaType);
        Assert.Equal("1", error.SagaId.Value);
        Assert.Equal(typeof(ConversationPaid), error.MessageType);
    }

    [Fact]
    public async Task duplicate_start_throws_saga_already_exists()
    {
        var store = new InMemorySagaStore();
        IMessageBus bus = Bus(store);

        await bus.InvokeAsync(new ConversationStarted(1));

        SagaAlreadyExists error = await Assert.ThrowsAsync<SagaAlreadyExists>(
            () => bus.InvokeAsync(new ConversationStarted(1)));

        Assert.Equal(typeof(ConversationSaga), error.SagaType);
        Assert.Equal("1", error.SagaId.Value);
    }

    [Fact]
    public async Task duplicate_start_after_complete_still_throws_saga_already_exists()
    {
        var store = new InMemorySagaStore();
        IMessageBus bus = Bus(store);

        await bus.InvokeAsync(new ConversationStarted(1));
        await bus.InvokeAsync(new ConversationPaid(1));

        await Assert.ThrowsAsync<SagaAlreadyExists>(() => bus.InvokeAsync(new ConversationStarted(1)));
    }

    [Fact]
    public async Task throwing_start_persists_no_row_and_publishes_no_cascades()
    {
        var store = new InMemorySagaStore();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ThrowingStartSaga));
        var cascades = new RecordingCascadePublisher();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, cascades, sagas: store);

        HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
            () => bus.InvokeAsync(new ConversationStarted(1)));

        Assert.IsType<TimeoutException>(fault.InnerException);
        Assert.Null(store.Load(typeof(ThrowingStartSaga), new SagaId("1")));
        Assert.Empty(cascades.Published);
    }

    [Fact]
    public async Task throwing_handle_leaves_the_previous_row_unchanged()
    {
        var store = new InMemorySagaStore();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ThrowingHandleSaga));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, sagas: store);

        await bus.InvokeAsync(new ConversationStarted(1));
        await Assert.ThrowsAsync<HandlerFault>(() => bus.InvokeAsync(new ConversationPaid(1)));

        var saga = Assert.IsType<ThrowingHandleSaga>(store.Load(typeof(ThrowingHandleSaga), new SagaId("1")));
        Assert.Equal("started", saga.Token);
        Assert.False(saga.IsCompleted);
    }

    [Fact]
    public async Task handle_retry_reloads_the_previous_snapshot()
    {
        var store = new InMemorySagaStore();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(FlakyHandleSaga));
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Retry().Retry();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, executor: new Executor(policies), sagas: store);

        await bus.InvokeAsync(new ConversationStarted(1));
        await bus.InvokeAsync(new ConversationPaid(1));

        var saga = Assert.IsType<FlakyHandleSaga>(store.Load(typeof(FlakyHandleSaga), new SagaId("1")));
        Assert.Equal("paid-started", saga.Token);
        Assert.True(saga.IsCompleted);
    }

    [Fact]
    public async Task empty_saga_id_throws_saga_id_required()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(EmptyIdSaga));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, sagas: new InMemorySagaStore());

        SagaIdRequired error = await Assert.ThrowsAsync<SagaIdRequired>(
            () => bus.InvokeAsync(new EmptyIdStarted("")));

        Assert.Equal(typeof(EmptyIdSaga), error.SagaType);
        Assert.Equal(typeof(EmptyIdStarted), error.MessageType);
    }

    [Fact]
    public async Task saga_path_stamps_envelope_saga_id()
    {
        IMessageBus bus = Bus(new InMemorySagaStore());

        await bus.InvokeAsync(new ConversationStarted(7));

        Assert.Equal("7", ConversationSaga.LastEnvelopeSagaId);
    }

    [Fact]
    public async Task non_saga_fan_out_keeps_an_empty_saga_id()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ConversationSaga));
        catalog.Scan(typeof(ConversationObserver));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, sagas: new InMemorySagaStore());

        await bus.InvokeAsync(new ConversationStarted(7));

        Assert.Equal("7", ConversationSaga.LastEnvelopeSagaId);
        Assert.Equal("", ConversationObserver.LastSagaId);
    }

    [Fact]
    public async Task start_cascades_are_published_and_the_saga_instance_is_not()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ConversationSaga));
        var cascades = new RecordingCascadePublisher();
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, cascades, sagas: new InMemorySagaStore());

        await bus.InvokeAsync(new ConversationStarted(1));

        object published = Assert.Single(cascades.Published);
        Assert.IsType<ConversationWork>(published);
    }

    [Fact]
    public async Task cancelled_invoke_does_not_persist_a_start()
    {
        var store = new InMemorySagaStore();
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ConversationSaga));
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(catalog, sagas: store);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => bus.InvokeAsync(new ConversationStarted(1), cancelled.Token));

        Assert.Null(store.Load(typeof(ConversationSaga), new SagaId("1")));
    }

    [Fact]
    public async Task missing_catalog_handler_is_still_handler_not_found()
    {
        IMessageBus bus = new Cabbie.Application.Mediator.Mediator(new HandlerCatalog(), sagas: new InMemorySagaStore());

        HandlerNotFound missing = await Assert.ThrowsAsync<HandlerNotFound>(
            () => bus.InvokeAsync(new ConversationStarted(1)));

        Assert.Equal(typeof(ConversationStarted), missing.MessageType);
    }

    private static IMessageBus Bus(ISagaStore store)
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ConversationSaga));
        return new Cabbie.Application.Mediator.Mediator(catalog, sagas: store);
    }
}

public sealed record ConversationStarted([property: Cabbie.Domain.Sagas.SagaIdentity] int Id);

public sealed record ConversationPaid([property: Cabbie.Domain.Sagas.SagaIdentity] int Id);

public sealed record ConversationTimeout([property: Cabbie.Domain.Sagas.SagaIdentity] int Id);

public sealed record ConversationWork(int Id);

public sealed record EmptyIdStarted([property: Cabbie.Domain.Sagas.SagaIdentity] string Id);

public sealed class ConversationSaga : Saga
{
    public static bool TimeoutHandled;
    public static bool TimeoutNotFound;
    public static string LastEnvelopeSagaId = "";

    public string Token { get; set; } = "";

    public object Start(ConversationStarted message, Envelope envelope)
    {
        LastEnvelopeSagaId = envelope.SagaId.Value;
        Token = "started";
        return (this, new ConversationWork(message.Id));
    }

    public void Handle(ConversationPaid message)
    {
        Token = "paid-" + Token;
        MarkCompleted();
    }

    public void Handle(ConversationTimeout message)
    {
        TimeoutHandled = true;
    }

    public void NotFound(ConversationTimeout message)
    {
        TimeoutNotFound = true;
    }
}

public sealed class ConversationObserver
{
    public static string LastSagaId = "";

    public void Handle(ConversationStarted message, Envelope envelope)
    {
        LastSagaId = envelope.SagaId.Value;
    }
}

public sealed class PaidOnlySaga : Saga
{
    public void Handle(ConversationPaid message)
    {
    }
}

public sealed class ThrowingStartSaga : Saga
{
    public ConversationWork Start(ConversationStarted message)
    {
        throw new TimeoutException("start failed");
    }
}

public sealed class ThrowingHandleSaga : Saga
{
    public string Token { get; set; } = "";

    public void Start(ConversationStarted message)
    {
        Token = "started";
    }

    public void Handle(ConversationPaid message)
    {
        Token = "dirty";
        throw new TimeoutException("handle failed");
    }
}

public sealed class FlakyHandleSaga : Saga
{
    public string Token { get; set; } = "";

    public void Start(ConversationStarted message)
    {
        Token = "started";
    }

    public void Handle(ConversationPaid message, Envelope envelope)
    {
        Token = "dirty";
        if (envelope.Attempts.Value < 3)
        {
            throw new TimeoutException("flaky pay");
        }

        Token = "paid-started";
        MarkCompleted();
    }
}

public sealed class EmptyIdSaga : Saga
{
    public void Start(EmptyIdStarted message)
    {
    }
}
