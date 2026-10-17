namespace Cabbie.Application.Execution;

/// <summary>
/// A handler attempt outlived its timeout and its cancellation token fired. Derives from
/// <see cref="TimeoutException"/>, so <c>OnException&lt;TimeoutException&gt;()</c> matches it and
/// <c>OnException&lt;HandlerTimedOut&gt;()</c> matches it more specifically.
/// </summary>
public sealed class HandlerTimedOut : TimeoutException
{
    public HandlerTimedOut(Type handlerType, TimeSpan timeout, OperationCanceledException inner)
        : base($"Handler {handlerType?.FullName} exceeded its {timeout} timeout.", inner)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        HandlerType = handlerType;
        Timeout = timeout;
    }

    public Type HandlerType { get; }

    public TimeSpan Timeout { get; }
}
