using Cabbie.Application.Discovery;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Middleware;

/// <summary>
/// Russian-doll wrapper around a handler attempt (inner) or the retry loop (outer).
/// Call <paramref name="next"/> exactly once. Do not skip Handle or replace its result.
/// Envelope is read-only; Execution owns Attempts.
/// </summary>
public interface IMessageMiddleware
{
    Task<object?> InvokeAsync(
        Envelope envelope,
        DiscoveredHandler handler,
        Func<Task<object?>> next,
        CancellationToken cancellationToken);
}
