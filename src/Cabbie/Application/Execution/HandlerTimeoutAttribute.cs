using System.Reflection;

namespace Cabbie.Application.Execution;

/// <summary>
/// Per-attempt timeout for a handler class or method. A method attribute beats a class attribute,
/// which beats <c>CabbieOptions.HandlerTimeout</c>. Cooperative: the handler's CancellationToken
/// is cancelled, and a handler that ignores it and finishes still succeeds.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HandlerTimeoutAttribute : Attribute
{
    public HandlerTimeoutAttribute(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(milliseconds, 0);
        Timeout = TimeSpan.FromMilliseconds(milliseconds);
    }

    public TimeSpan Timeout { get; }

    /// <summary>
    /// The timeout declared for <paramref name="method"/>, else for <paramref name="handlerType"/>, else null.
    /// </summary>
    internal static TimeSpan? For(MethodInfo method, Type handlerType) =>
        (method.GetCustomAttribute<HandlerTimeoutAttribute>()
            ?? handlerType.GetCustomAttribute<HandlerTimeoutAttribute>(inherit: true))?.Timeout;
}
