using System.Reflection;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Discovery;

/// <summary>
/// Whether a method is a handler by convention. Handle / HandleAsync / Consume / ConsumeAsync / Start / StartAsync.
/// First parameter is the message; extra parameters are injection slots: Envelope, CancellationToken,
/// or a service resolved from the per-message DI scope.
/// </summary>
public static class HandlerConvention
{
    private static readonly HashSet<string> HandlerNames =
    [
        "Handle",
        "HandleAsync",
        "Consume",
        "ConsumeAsync",
        "Start",
        "StartAsync"
    ];

    public static bool IsStart(string methodName) =>
        methodName is "Start" or "StartAsync";

    public static DiscoveredHandler? For(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (method.IsPublic is false)
        {
            return null;
        }

        if (!HandlerNames.Contains(method.Name))
        {
            return null;
        }

        if (method.IsGenericMethodDefinition is true)
        {
            return null;
        }

        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            return null;
        }

        Type? handlerType = method.DeclaringType;
        if (handlerType is null)
        {
            return null;
        }

        ParameterInfo[] slots = parameters[1..];
        foreach (ParameterInfo slot in slots)
        {
            EnsureSupportedSlot(handlerType, method.Name, slot);
        }

        return new DiscoveredHandler(
            method,
            handlerType,
            parameters[0].ParameterType,
            method.IsStatic,
            slots);
    }

    /// <summary>
    /// Any parameter type is an injection slot (Envelope, CancellationToken, or a scoped service);
    /// out/ref/params cannot be filled by the container. Reject at Scan so the user sees the reason
    /// before a real handler ever runs.
    /// </summary>
    private static void EnsureSupportedSlot(Type handlerType, string methodName, ParameterInfo slot)
    {
        if (slot.ParameterType.IsByRef || slot.IsDefined(typeof(ParamArrayAttribute), inherit: false))
        {
            throw new InvalidHandlerSignature(
                handlerType,
                $"Handler '{handlerType}.{methodName}' parameter '{slot.Name}' cannot be an out, ref, or params parameter.");
        }
    }
}
