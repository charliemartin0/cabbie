using System.Reflection;
using Cabbie.Domain.Sagas;
using Cabbie.Domain.Sagas.Validators;

namespace Cabbie.Tests.Domain.Sagas;

public sealed class TimeoutAttributeTests
{
    [Fact]
    public void timeout_minutes_become_a_timespan_delay()
    {
        var timeout = typeof(OrderTimeout).GetCustomAttribute<TimeoutAttribute>();

        Assert.NotNull(timeout);
        Assert.Equal(TimeSpan.FromMinutes(1), timeout.Delay);
    }

    [Fact]
    public void delay_is_metadata_not_a_running_timer()
    {
        var timeout = new TimeoutAttribute { Hours = 1, Minutes = 2, Seconds = 3, Milliseconds = 4 };

        Assert.Equal(new TimeSpan(0, 1, 2, 3, 4), timeout.Delay);
    }

    [Fact]
    public void all_zeros_fail_validation()
    {
        var result = new TimeoutAttributeValidator().Validate(new TimeoutAttribute());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("greater than zero", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void negative_parts_fail_validation()
    {
        var result = new TimeoutAttributeValidator().Validate(new TimeoutAttribute { Minutes = -1 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("not be negative", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void positive_delay_passes_validation()
    {
        var timeout = typeof(OrderTimeout).GetCustomAttribute<TimeoutAttribute>();

        var result = new TimeoutAttributeValidator().Validate(timeout!);

        Assert.True(result.IsValid);
    }
}
