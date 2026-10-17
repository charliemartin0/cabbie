using System.Reflection;
using Cabbie.Application.Discovery;
using Microsoft.Extensions.DependencyInjection;

namespace Cabbie.Application.Execution;

/// <summary>
/// Container lookups for one handler invocation. Every call receives the message scope's
/// provider, so constructor and method services share the same scoped instances.
/// </summary>
internal static class HandlerServices
{
    public static object Resolve(IServiceProvider services, ParameterInfo slot) =>
        services.GetService(slot.ParameterType) ?? throw new HandlerDependencyMissing(slot);

    /// <summary>
    /// ResolveTarget wins (saga paths). Otherwise the container, then ActivatorUtilities for
    /// handlers that were never registered. A missing constructor service is reported as
    /// <see cref="HandlerDependencyMissing"/>.
    /// </summary>
    public static object CreateTarget(IServiceProvider services, DiscoveredHandler handler)
    {
        if (handler.ResolveTarget is { } resolve)
        {
            return resolve(services)!;
        }

        try
        {
            return services.GetService(handler.HandlerType)
                ?? ActivatorUtilities.CreateInstance(services, handler.HandlerType);
        }
        catch (InvalidOperationException ex) when (ex is not HandlerDependencyMissing)
        {
            throw new HandlerDependencyMissing(handler.HandlerType, ex);
        }
    }
}
