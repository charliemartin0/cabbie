using System.Reflection;

namespace Cabbie.Application.Discovery;

/// <summary>
/// A scanned handler type is not a valid handler. Scan throws; it does not silently omit the method.
/// </summary>
public sealed class InvalidHandlerSignature : Exception
{
    public Type HandlerType { get; }

    public InvalidHandlerSignature(Type handlerType, string message)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        HandlerType = handlerType;
    }

    /// <summary>
    /// An injection slot that is neither <c>Envelope</c> nor <c>CancellationToken</c>:
    /// Cabbie fills only those two, so any other slot would arrive null at invocation.
    /// </summary>
    public InvalidHandlerSignature(Type handlerType, string methodName, ParameterInfo parameter)
        : this(handlerType, BuildMessage(handlerType, methodName, parameter))
    {
    }

    private static string BuildMessage(Type handlerType, string methodName, ParameterInfo parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(parameter);
        return $"Handler '{handlerType}.{methodName}' parameter '{parameter.Name}' is not Envelope or CancellationToken; DI resolution is not registered.";
    }
}
