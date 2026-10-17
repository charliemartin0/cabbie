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
    private readonly bool _unmatched;

    public OnExceptionExpression(ErrorPolicyCatalog catalog, Type exceptionType, Type? messageType = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(exceptionType);
        _catalog = catalog;
        _exceptionType = exceptionType;
        _messageType = messageType;
    }

    internal OnExceptionExpression(ErrorPolicyCatalog catalog)
    {
        _catalog = catalog;
        _exceptionType = typeof(Exception);
        _unmatched = true;
    }

    public OnExceptionExpression Then => this;

    private void Add(ErrorAction action)
    {
        if (_unmatched)
        {
            _catalog.RegisterDefault(action);
        }
        else
        {
            _catalog.Register(_exceptionType, _messageType, action);
        }
    }

    public OnExceptionExpression Retry()
    {
        Add(new Retry());
        return this;
    }

    public OnExceptionExpression RetryWithCooldown(params TimeSpan[] delays)
    {
        ArgumentNullException.ThrowIfNull(delays);
        foreach (TimeSpan delay in delays)
        {
            Add(new RetryWithCooldown(delay));
        }

        return this;
    }

    public OnExceptionExpression MoveToErrorQueue()
    {
        Add(new MoveToErrorQueue());
        return this;
    }

    public OnExceptionExpression Requeue()
    {
        Add(new Requeue());
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

            Add(new ScheduleRetry(delay));
        }

        return this;
    }

    public OnExceptionExpression Discard()
    {
        Add(new Discard());
        return this;
    }
}
