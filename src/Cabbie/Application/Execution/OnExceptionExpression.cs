using Cabbie.Domain.Errors.ValueObjects;

namespace Cabbie.Application.Execution;

/// <summary>
/// Fluent chain for one exception type (and optional message type). Records actions; it does not invoke.
/// </summary>
public sealed class OnExceptionExpression
{
    private readonly ErrorPolicyCatalog _catalog;
    private readonly Type _exceptionType;
    private readonly Type? _messageType;

    public OnExceptionExpression(ErrorPolicyCatalog catalog, Type exceptionType, Type? messageType = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(exceptionType);
        _catalog = catalog;
        _exceptionType = exceptionType;
        _messageType = messageType;
    }

    public OnExceptionExpression Then => this;

    public OnExceptionExpression Retry()
    {
        _catalog.Register(_exceptionType, _messageType, new Retry());
        return this;
    }

    public OnExceptionExpression RetryWithCooldown(params TimeSpan[] delays)
    {
        ArgumentNullException.ThrowIfNull(delays);
        foreach (TimeSpan delay in delays)
        {
            _catalog.Register(_exceptionType, _messageType, new RetryWithCooldown(delay));
        }

        return this;
    }

    public OnExceptionExpression MoveToErrorQueue()
    {
        _catalog.Register(_exceptionType, _messageType, new MoveToErrorQueue());
        return this;
    }

    public OnExceptionExpression Requeue()
    {
        _catalog.Register(_exceptionType, _messageType, new Requeue());
        return this;
    }

    public OnExceptionExpression ScheduleRetry(params TimeSpan[] delays)
    {
        ArgumentNullException.ThrowIfNull(delays);
        if (delays.Length == 0)
        {
            throw new ArgumentException("ScheduleRetry requires at least one delay.", nameof(delays));
        }

        foreach (TimeSpan delay in delays)
        {
            if (delay <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delays), delay, "ScheduleRetry delay must be greater than zero.");
            }

            _catalog.Register(_exceptionType, _messageType, new ScheduleRetry(delay));
        }

        return this;
    }

    public OnExceptionExpression Discard()
    {
        _catalog.Register(_exceptionType, _messageType, new Discard());
        return this;
    }
}
