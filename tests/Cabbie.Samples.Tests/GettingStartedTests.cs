using GettingStarted;

namespace Cabbie.Samples.Tests;

public sealed class GettingStartedTests
{
    [Fact]
    public async Task place_order_runs_its_handler_and_the_order_placed_cascade()
    {
        IReadOnlyList<int> placed = await GettingStartedHost.RunAsync();

        Assert.Equal([1], placed);
    }
}
