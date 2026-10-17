using System.Reflection;
using Cabbie.Application.Execution;

namespace Cabbie.Application.Discovery;

/// <summary>
/// A public handler method found by convention. Extra parameters are injection slots, not resolved here.
/// </summary>
public sealed record DiscoveredHandler(
    MethodInfo Method,
    Type HandlerType,
    Type MessageClrType,
    bool IsStatic,
    IReadOnlyList<ParameterInfo> InjectionSlots,
    Func<object?>? ResolveTarget = null,
    bool Scheduled = false)
{
    /// <summary>
    /// Compiled at Scan time by <see cref="HandlerInvokerCompiler"/>. Null for handlers
    /// constructed at runtime (e.g. the saga NotFound miss path) — the Executor falls
    /// back to reflection for those.
    /// </summary>
    internal HandlerInvoker? CachedInvoker { get; init; }
}