using System.Reflection;

namespace Cabbie.Application.Execution;

/// <summary>
/// A handler could not be built, or a handler method parameter could not be filled, because a
/// service is not registered in the container. Names the handler and the service so the
/// missing registration is found without a debugger.
/// </summary>
public sealed class HandlerDependencyMissing : InvalidOperationException
{
    public Type HandlerType { get; }

    /// <summary>
    /// The missing service for a method parameter. Null for constructor failures — the inner
    /// exception from the container names the constructor parameter.
    /// </summary>
    public Type? ServiceType { get; }

    public HandlerDependencyMissing(Type handlerType, InvalidOperationException inner)
        : base(Describe(handlerType, inner), inner)
    {
        HandlerType = handlerType;
    }

    public HandlerDependencyMissing(ParameterInfo slot)
        : base(Describe(slot))
    {
        HandlerType = slot.Member.DeclaringType!;
        ServiceType = slot.ParameterType;
    }

    private static string Describe(Type handlerType, InvalidOperationException inner)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentNullException.ThrowIfNull(inner);
        return $"Cabbie could not construct handler '{handlerType.FullName}': {inner.Message}";
    }

    private static string Describe(ParameterInfo slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return $"Handler '{slot.Member.DeclaringType!.FullName}.{slot.Member.Name}' parameter '{slot.Name}' needs service '{slot.ParameterType.FullName}', which is not registered in the container.";
    }
}
