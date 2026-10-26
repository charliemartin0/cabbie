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

    private static readonly AsyncLocal<Tracker?> Current = new();

    /// <summary>
    /// Called by the Executor when a handler's policy diverted its work out of this delivery
    /// (requeued or parked a retry). A whole-envelope redelivery would then run that handler again.
    /// </summary>
    internal static void NoteDiverted()
    {
        if (Current.Value is { } tracker)
        {
            tracker.Diverted = true;
        }
    }

    /// <summary>
    /// Run every handler. A <see cref="HandlerFault"/> is held until the rest ran, then
    /// <see cref="Rethrow"/> decides. Any other exception, cancellation included, propagates at once.
    /// When another handler already requeued or parked its retry, an unresolved fault must not
    /// nack the whole envelope (that would run the diverted handler twice): the failing handler is
    /// redelivered on its own through <see cref="Executor.RedeliverToHandlerAsync"/> instead.
    /// </summary>
    internal static async Task RunAsync(
        IReadOnlyList<DiscoveredHandler> handlers,
        Envelope envelope,
        Executor executor,
        Func<DiscoveredHandler, Task> run,
        CancellationToken cancellationToken)
    {
        var tracker = new Tracker();
        Tracker? outer = Current.Value;
        Current.Value = tracker;
        List<(DiscoveredHandler Handler, HandlerFault Fault)>? faults = null;
        try
        {
            foreach (DiscoveredHandler handler in handlers)
            {
                try
                {
                    await run(handler);
                }
                catch (HandlerFault fault)
                {
                    (faults ??= []).Add((handler, fault));
                }
            }
        }
        finally
        {
            Current.Value = outer;
        }

        if (faults is null)
        {
            return;
        }

        if (tracker.Diverted && handlers.Count > 1 && envelope.Handler is null)
        {
            var remaining = new List<HandlerFault>();
            foreach ((DiscoveredHandler handler, HandlerFault fault) in faults)
            {
                if (fault.DeadLettered || !await executor.RedeliverToHandlerAsync(envelope, handler, cancellationToken))
                {
                    remaining.Add(fault);
                }
            }

            Rethrow(remaining);
            return;
        }

        Rethrow(faults.ConvertAll(entry => entry.Fault));
    }

    private sealed class Tracker
    {
        public bool Diverted { get; set; }
    }

    /// <summary>
    /// Rethrow the fault that decides the envelope's outcome: the first one the policy did not
    /// resolve, else the first dead-lettered one (so a transport acks). No faults: nothing.
    /// </summary>
    internal static void Rethrow(List<HandlerFault>? faults)
    {
        if (faults is null || faults.Count == 0)
        {
            return;
        }

        HandlerFault toThrow = faults.Find(fault => !fault.DeadLettered) ?? faults[0];
        ExceptionDispatchInfo.Capture(toThrow).Throw();
    }
}
