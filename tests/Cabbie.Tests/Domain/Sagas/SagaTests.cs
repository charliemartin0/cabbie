using Cabbie.Domain.Sagas;

namespace Cabbie.Tests.Domain.Sagas;

public sealed class SagaTests
{
    [Fact]
    public void mark_completed_only_sets_the_flag()
    {
        var saga = new OrderSaga { Id = 1 };

        Assert.False(saga.IsCompleted);
        saga.MarkCompleted();
        Assert.True(saga.IsCompleted);
        Assert.Equal(1, saga.Id);
    }

    [Fact]
    public void saga_base_does_not_own_id()
    {
        Assert.Null(typeof(Saga).GetProperty("Id"));
        Assert.NotNull(typeof(OrderSaga).GetProperty("Id"));
    }
}
