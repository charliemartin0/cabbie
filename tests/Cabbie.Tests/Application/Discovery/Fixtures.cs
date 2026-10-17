using Cabbie.Domain.Messaging;
using Cabbie.Domain.Sagas;
using Cabbie.Tests.Domain;

namespace Cabbie.Tests.Application.Discovery;

public sealed class PlaceOrderHandler
{
    public void Handle(PlaceOrder message)
    {
    }
}

public sealed class DiscoveredOrderSaga : Saga
{
    public int? Id { get; set; }

    public object Start(PlaceOrder message) => this;
}

public sealed class TimeoutWithNotFoundSaga : Saga
{
    public void Handle(OrderTimeout message)
    {
    }

    public void NotFound(OrderTimeout message)
    {
    }
}

public sealed record LogIncident;

public static class LogIncidentConsumer
{
    public static void Consume(LogIncident message)
    {
    }
}

public sealed record PingAsync;

public sealed class PingAsyncHandler
{
    public Task HandleAsync(PingAsync message) => Task.CompletedTask;
}

public sealed record ThingHappened;

public sealed class ThingHappenedConsumer
{
    public Task ConsumeAsync(ThingHappened message) => Task.CompletedTask;
}

public sealed record StartAsyncMessage(int Id);

public sealed class StartAsyncSaga : Saga
{
    public Task StartAsync(StartAsyncMessage message) => Task.CompletedTask;
}

public sealed class FirstChargePaymentHandler
{
    public void Handle(ChargePayment message)
    {
    }
}

public sealed class SecondChargePaymentHandler
{
    public static void Consume(ChargePayment message)
    {
    }
}

public sealed record HiddenMessage;

public sealed class HiddenHandler
{
    private void Handle(HiddenMessage message)
    {
    }
}

public sealed class NotAHandler
{
    public void Process(PlaceOrder message)
    {
    }
}

public sealed class ParameterlessHandler
{
    public void Handle()
    {
    }
}

public sealed class GenericHandler
{
    public void Handle<T>(T message)
    {
    }
}

public abstract class AbstractPlaceOrderHandler
{
    public void Handle(PlaceOrder message)
    {
    }
}

public sealed class FirstSharedAliasHandler
{
    public void Handle(FirstSharedAlias message)
    {
    }
}

public sealed class SecondSharedAliasHandler
{
    public void Handle(SecondSharedAlias message)
    {
    }
}
