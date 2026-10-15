using Cabbie.Application.Discovery;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Execution;

/// <summary>
/// One call per handler attempt (success or throw). Tracking uses this for Executed bags.
/// </summary>
public interface IHandlerAttemptObserver
{
    void OnAttempt(Envelope envelope, DiscoveredHandler handler, Exception? exception);
}
