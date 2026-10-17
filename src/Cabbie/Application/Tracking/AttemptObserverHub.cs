using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Tracking;

/// <summary>
/// Forwards handler attempts to the observer currently bound for a tracked session.
/// The host shares one <see cref="Executor"/> between the Mediator and the local queues;
/// the Mediator binds its session logger into this hub so tracked sessions keep recording
/// Executed bags without owning the Executor. Forwarding to no observer is a no-op.
/// </summary>
public sealed class AttemptObserverHub : IHandlerAttemptObserver
{
    private volatile IHandlerAttemptObserver? _current;

    public void Bind(IHandlerAttemptObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _current = observer;
    }

    public void OnAttempt(Envelope envelope, DiscoveredHandler handler, Exception? exception)
    {
        _current?.OnAttempt(envelope, handler, exception);
    }
}