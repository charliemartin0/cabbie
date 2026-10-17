using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Errors.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Execution;

public sealed class ExecutorResolveTargetTests
{
    public ExecutorResolveTargetTests()
    {
        ResolveTargetFlakyHandler.SeenAttempts.Clear();
    }

    [Fact]
    public async Task resolve_target_is_called_once_per_attempt()
    {
        int calls = 0;
        Envelope envelope = EnvelopeFactory.Create(
            message: new Message(new ChargePayment(1)),
            messageType: MessageTypeNaming.For(typeof(ChargePayment)));
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Retry().Retry().Then.MoveToErrorQueue();
        var executor = new Executor(policies);
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(ResolveTargetFlakyHandler));
        DiscoveredHandler handler = Assert.Single(catalog.Handlers);

        object? result = await executor.InvokeAsync(
            envelope,
            handler with
            {
                ResolveTarget = _ =>
                {
                    calls++;
                    return new ResolveTargetFlakyHandler();
                }
            });

        Assert.Equal(3, calls);
        Assert.Equal([1, 2, 3], ResolveTargetFlakyHandler.SeenAttempts);
        Assert.IsType<ChargePayment>(result);
    }
}

public sealed class ResolveTargetFlakyHandler
{
    public static List<int> SeenAttempts { get; } = [];

    public ChargePayment Handle(ChargePayment payment, Envelope envelope)
    {
        SeenAttempts.Add(envelope.Attempts.Value);
        if (envelope.Attempts.Value < 3)
        {
            throw new TimeoutException("Payment gateway timeout");
        }

        return payment;
    }
}
