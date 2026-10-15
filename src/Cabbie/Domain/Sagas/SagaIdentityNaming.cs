using System.Globalization;
using System.Reflection;
using Cabbie.Domain.Sagas.ValueObjects;

namespace Cabbie.Domain.Sagas;

/// <summary>
/// Which saga instance a message belongs to. [SagaIdentity], then {SagaType}Id, then Id. No match is empty.
/// </summary>
public static class SagaIdentityNaming
{
    public static SagaId For(object message, Type sagaType)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sagaType);

        PropertyInfo? property = IdentityProperty(message.GetType(), sagaType);
        return property is null ? new SagaId("") : ToSagaId(property.GetValue(message));
    }

    public static bool CanCorrelate(Type messageType, Type sagaType)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(sagaType);

        return IdentityProperty(messageType, sagaType) is not null;
    }

    private static PropertyInfo? IdentityProperty(Type messageType, Type sagaType)
    {
        PropertyInfo[] properties = messageType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        PropertyInfo? attributed = Array.Find(
            properties,
            property => property.GetCustomAttribute<SagaIdentityAttribute>(inherit: true) is not null);
        if (attributed is not null)
        {
            return attributed;
        }

        PropertyInfo? bySagaTypeName = Array.Find(properties, property => property.Name == sagaType.Name + "Id");
        if (bySagaTypeName is not null)
        {
            return bySagaTypeName;
        }

        return Array.Find(properties, property => property.Name == "Id");
    }

    private static SagaId ToSagaId(object? value)
    {
        if (value is null)
        {
            return new SagaId("");
        }

        return new SagaId(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
    }
}
