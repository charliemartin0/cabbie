using System.Globalization;
using Cabbie.Application.Bus;
using Cabbie.Application.Scheduling;
using Cabbie.Tests.Domain;

namespace Cabbie.Tests.Application.Scheduling;

public sealed class DelayedDeliveryTests
{
    [Fact]
    public void timeout_attribute_is_sent_at_plus_delay()
    {
        DateTimeOffset sent = DateTimeOffset.Parse("2026-09-04T12:00:00Z", CultureInfo.InvariantCulture);

        DateTimeOffset? due = DelayedDelivery.DueAt(typeof(OrderTimeout), options: null, sent);

        Assert.Equal(sent.AddMinutes(1), due);
    }

    [Fact]
    public void explicit_delay_overrides_timeout()
    {
        DateTimeOffset sent = DateTimeOffset.Parse("2026-09-04T12:00:00Z", CultureInfo.InvariantCulture);
        var options = new DeliveryOptions { Delay = TimeSpan.FromMinutes(5) };

        DateTimeOffset? due = DelayedDelivery.DueAt(typeof(OrderTimeout), options, sent);

        Assert.Equal(sent.AddMinutes(5), due);
    }

    [Fact]
    public void zero_delay_bypasses_the_hold_even_with_timeout()
    {
        DateTimeOffset sent = DateTimeOffset.Parse("2026-09-04T12:00:00Z", CultureInfo.InvariantCulture);
        var options = new DeliveryOptions { Delay = TimeSpan.Zero };

        Assert.Null(DelayedDelivery.DueAt(typeof(OrderTimeout), options, sent));
    }

    [Fact]
    public void absolute_until_equal_to_sent_at_is_now()
    {
        DateTimeOffset sent = DateTimeOffset.Parse("2026-09-04T12:00:00Z", CultureInfo.InvariantCulture);
        var options = new DeliveryOptions { Until = sent };

        Assert.Null(DelayedDelivery.DueAt(typeof(PlaceOrder), options, sent));
    }

    [Fact]
    public void absolute_until_in_the_future_is_that_instant()
    {
        DateTimeOffset sent = DateTimeOffset.Parse("2026-09-04T12:00:00Z", CultureInfo.InvariantCulture);
        DateTimeOffset until = sent.AddHours(2);
        var options = new DeliveryOptions { Until = until };

        Assert.Equal(until, DelayedDelivery.DueAt(typeof(PlaceOrder), options, sent));
    }

    [Fact]
    public void delay_and_until_together_are_ambiguous()
    {
        var options = new DeliveryOptions
        {
            Delay = TimeSpan.FromMinutes(1),
            Until = DateTimeOffset.UtcNow.AddMinutes(1)
        };

        Assert.Throws<AmbiguousDeliveryOptions>(
            () => DelayedDelivery.DueAt(typeof(PlaceOrder), options, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void charge_payment_without_options_is_immediate()
    {
        Assert.Null(DelayedDelivery.DueAt(typeof(ChargePayment), options: null, DateTimeOffset.UtcNow));
    }
}
