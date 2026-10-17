using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Application.Scheduling;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Errors.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Infrastructure.Scheduling;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Execution;

public sealed class ExecutorScheduleRetryTests
{
    public ExecutorScheduleRetryTests()
    {
        ScheduleRetryChargeHandler.SeenAttempts.Clear();
    }

    [Fact]
    public async Task schedule_retry_on_invoke_is_a_named_error_and_does_not_park()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().ScheduleRetry(TimeSpan.FromSeconds(5));
        var executor = new Executor(policies, scheduled: hold);

        await Assert.ThrowsAsync<ScheduleRetryNotSupportedOnInvoke>(
            () => executor.InvokeAsync(
                ChargePaymentEnvelope(),
                HandlerFor<ScheduleRetryChargeHandler>()));

        Assert.Empty(hold.Peek());
        Assert.Equal([1], ScheduleRetryChargeHandler.SeenAttempts);
    }

    [Fact]
    public async Task schedule_retry_on_scheduled_parks_the_same_envelope_with_attempts_incremented()
    {
        var hold = new InMemoryScheduledEnvelopeHold();
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().ScheduleRetry(TimeSpan.FromMinutes(1));
        var executor = new Executor(policies, scheduled: hold);
        Envelope envelope = ChargePaymentEnvelope();

        object? result = await executor.InvokeAsync(
            envelope,
            HandlerFor<ScheduleRetryChargeHandler>() with { Scheduled = true });

        Assert.Null(result);
        var parked = Assert.Single(hold.Peek());
        Assert.Equal(envelope.Id, parked.Id);
        Assert.Equal(2, parked.Attempts.Value);
        Assert.True(parked.DeliverBy.Value > DateTimeOffset.UtcNow);
        Assert.Equal([1], ScheduleRetryChargeHandler.SeenAttempts);
    }

    private static Envelope ChargePaymentEnvelope() =>
        EnvelopeFactory.Create(
            message: new Message(new ChargePayment(1)),
            messageType: MessageTypeNaming.For(typeof(ChargePayment)));

    private static DiscoveredHandler HandlerFor<THandler>()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(THandler));
        return Assert.Single(catalog.Handlers);
    }
}

public sealed class ScheduleRetryChargeHandler
{
    public static List<int> SeenAttempts { get; } = [];

    public void Handle(ChargePayment payment, Envelope envelope)
    {
        SeenAttempts.Add(envelope.Attempts.Value);
        throw new TimeoutException("Payment gateway timeout");
    }
}
