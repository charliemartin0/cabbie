using System.Reflection;
using Cabbie.Application.Discovery;

namespace Cabbie.Application.Sagas;

/// <summary>
/// Miss-path method on a saga type. Not a catalog handler.
/// </summary>
public static class NotFoundConvention
{
    private static readonly BindingFlags Methods =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    public static DiscoveredHandler? For(Type sagaType, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(sagaType);
        ArgumentNullException.ThrowIfNull(messageType);

        MethodInfo? method = Find(sagaType, "NotFound", messageType)
            ?? Find(sagaType, "NotFoundAsync", messageType);
        if (method is null)
        {
            return null;
        }

        ParameterInfo[] parameters = method.GetParameters();
        return new DiscoveredHandler(
            method,
            sagaType,
            messageType,
            IsStatic: false,
            parameters[1..]);
    }

    private static MethodInfo? Find(Type sagaType, string name, Type messageType) =>
        sagaType.GetMethods(Methods).FirstOrDefault(method =>
            method.Name == name
            && method.GetParameters() is { Length: > 0 } parameters
            && parameters[0].ParameterType == messageType);
}
