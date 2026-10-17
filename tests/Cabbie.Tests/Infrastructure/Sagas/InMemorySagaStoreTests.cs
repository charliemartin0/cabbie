using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Infrastructure.Sagas;
using Cabbie.Tests.Application.Sagas;

namespace Cabbie.Tests.Infrastructure.Sagas;

public sealed class InMemorySagaStoreTests
{
    [Fact]
    public void load_returns_a_clone_not_the_saved_instance()
    {
        var store = new InMemorySagaStore();
        var saga = new ConversationSaga { Token = "started" };
        var id = new SagaId("1");

        store.Save(typeof(ConversationSaga), id, saga);
        saga.Token = "mutated-after-save";

        var loaded = Assert.IsType<ConversationSaga>(store.Load(typeof(ConversationSaga), id));
        Assert.Equal("started", loaded.Token);
        Assert.NotSame(saga, loaded);

        loaded.Token = "mutated-after-load";
        var loadedAgain = Assert.IsType<ConversationSaga>(store.Load(typeof(ConversationSaga), id));
        Assert.Equal("started", loadedAgain.Token);
        Assert.NotSame(loaded, loadedAgain);
    }

    [Fact]
    public void load_missing_id_is_null()
    {
        var store = new InMemorySagaStore();

        Assert.Null(store.Load(typeof(ConversationSaga), new SagaId("missing")));
    }
}
