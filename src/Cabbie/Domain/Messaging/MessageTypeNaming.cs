using System.Reflection;
using Cabbie.Domain.Messaging.ValueObjects;

namespace Cabbie.Domain.Messaging;

/// <summary>
/// Stable wire name for a CLR message type. MessageIdentity alias if present, otherwise FullName.
/// </summary>

public static class MessageTypeNaming
{
    public static MessageType For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        MessageIdentityAttribute? identityAttribute = type.GetCustomAttribute<MessageIdentityAttribute>(inherit: false);
        if (identityAttribute != null)
        {
            return new MessageType(identityAttribute.Alias);
        }

        return new MessageType(type.FullName ?? type.Name);

    }
}