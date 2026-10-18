using System.Runtime.ExceptionServices;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Bus;

/// <summary>
/// Fan-out isolation shared by <see cref="MessageDelivery"/> and the Mediator: a handler-targeted
/// envelope runs only that handler, and one handler's <see cref="HandlerFault"/> never skips the others.
/// </summary>
internal static class FanOut
{
    internal static IReadOnlyList<DiscoveredHandler> Select(IReadOnlyList<DiscoveredHandler> all, Envelope envelope)
    {
        if (envelope.Handler is not { } target)
        {
            return all;
        }

        return all.Where(handler => handler.Name == target.Value).ToList();
    }

    /// <summary>
    /// Run every handler. A <see cref="HandlerFault"/> is held until the rest ran, then
    /// <see cref="Rethrow"/> decides. Any other exception, cancellation included, propagates at once.
    /// </summary>
    internal static async Task RunAsync(IReadOnlyList<DiscoveredHandler> handlers, Func<DiscoveredHandler, Task> run)
    {
        List<HandlerFault>? faults = null;
        foreach (DiscoveredHandler handler in handlers)
        {
            try
            {
                await run(handler);
            }
            catch (HandlerFault fault)
            {
                (faults ??= []).Add(fault);
            }
        }

        Rethrow(faults);
    }

    /// <summary>
    /// Rethrow the fault that decides the envelope's outcome: the first one the policy did not
    /// resolve, else the first dead-lettered one (so a transport acks). No faults: nothing.
    /// </summary>
    internal static void Rethrow(List<HandlerFault>? faults)
    {
        if (faults is null)
        {
            return;
        }

        HandlerFault toThrow = faults.Find(fault => !fault.DeadLettered) ?? faults[0];
        ExceptionDispatchInfo.Capture(toThrow).Throw();
    }
}
