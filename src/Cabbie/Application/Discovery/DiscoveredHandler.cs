using System.Reflection;
using Cabbie.Application.Execution;

namespace Cabbie.Application.Discovery;

/// <summary>
/// A public handler method found by convention. Extra parameters are injection slots: Envelope, CancellationToken, or scoped services.
/// </summary>
public sealed record DiscoveredHandler(
    MethodInfo Method,
    Type HandlerType,
    Type MessageClrType,
    bool IsStatic,
    IReadOnlyList<ParameterInfo> InjectionSlots,
    Func<IServiceProvider, object?>? ResolveTarget = null,
    bool Scheduled = false)
{
    /// <summary>Stable per-message handler identity carried on a handler-targeted envelope.</summary>
    public string Name => $"{HandlerType.FullName}.{Method.Name}";

    /// <summary>
    /// Compiled at Scan time by <see cref="HandlerInvokerCompiler"/>. Null for handlers
    /// constructed at runtime (e.g. the saga NotFound miss path) — the Executor falls
    /// back to reflection for those.
    /// </summary>
    internal HandlerInvoker? CachedInvoker { get; init; }

    /// <summary>
    /// Per-attempt timeout from <see cref="HandlerTimeoutAttribute"/> (method, else type), cached at
    /// Scan time. Null falls back to the host-wide <c>CabbieOptions.HandlerTimeout</c>.
    /// </summary>
    internal TimeSpan? Timeout { get; init; }
}
