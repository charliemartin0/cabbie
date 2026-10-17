using System.Reflection;

namespace Cabbie.Application.Execution;

/// <summary>
/// A handler could not be built, or a handler method parameter could not be filled, because a
/// service is not registered in the container. Names the handler, the parameter and the
/// service so the missing registration is found without a debugger.
/// </summary>
public sealed class HandlerDependencyMissing : InvalidOperationException
{
    public Type HandlerType { get; }

    /// <summary>
    /// The unregistered service type of the offending constructor or method parameter.
    /// </summary>
    public Type ServiceType { get; }

    /// <param name="parameter">A constructor or handler method parameter.</param>
    /// <param name="handlerType">
    /// The handler type. Defaults to the parameter's declaring type; pass it when the method is
    /// inherited from a base class.
    /// </param>
    public HandlerDependencyMissing(ParameterInfo parameter, Type? handlerType = null, Exception? inner = null)
        : base(Describe(parameter, handlerType), inner)
    {
        HandlerType = handlerType ?? parameter.Member.DeclaringType!;
        ServiceType = parameter.ParameterType;
    }

    private static string Describe(ParameterInfo parameter, Type? handlerType)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        string handler = (handlerType ?? parameter.Member.DeclaringType!).FullName!;
        string where = parameter.Member is ConstructorInfo
            ? $"Handler '{handler}' constructor parameter '{parameter.Name}'"
            : $"Handler '{handler}.{parameter.Member.Name}' parameter '{parameter.Name}'";
        return $"{where} needs service '{parameter.ParameterType.FullName}', which is not registered in the container.";
    }
}
