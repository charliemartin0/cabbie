using Cabbie.Application.Discovery;
using Cabbie.Tests.Handlers;

namespace Cabbie.Tests.Application.Execution;

/// <summary>
/// Prove-with for Perf #1: the compiled handler invoker is baked at Scan time and
/// cached on the DiscoveredHandler — not rebuilt per lookup or per invocation.
/// </summary>
public sealed class HandlerInvokerTests
{
    [Fact]
    public void discovered_handler_carries_a_cached_invoker_after_scan()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ChargePaymentHandler));

        var first = Assert.Single(
            Assert.IsType<FoundHandlers>(catalog.Lookup(typeof(ChargePayment))).Handlers);
        Assert.NotNull(first.CachedInvoker);

        // A second lookup returns the same cached handler instance — same invoker.
        var second = Assert.Single(
            Assert.IsType<FoundHandlers>(catalog.Lookup(typeof(ChargePayment))).Handlers);
        Assert.Same(first.CachedInvoker, second.CachedInvoker);
    }
}