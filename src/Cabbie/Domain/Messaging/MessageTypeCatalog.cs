using Cabbie.Domain.Messaging.ValueObjects;

namespace Cabbie.Domain.Messaging;

/// <summary>
/// Bidirectional map of CLR message types to their wire names. Unknown names are a lookup result, not an exception.
/// </summary>
public sealed class MessageTypeCatalog
{
    private readonly List<(Type Type, MessageType Name)> _registrations = [];
    private readonly Dictionary<Type, MessageType> _byType = [];
    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);

    public IReadOnlyList<(Type Type, MessageType Name)> Registrations => _registrations;

    public void Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_byType.ContainsKey(type))
        {
            return;
        }

        MessageType name = MessageTypeNaming.For(type);
        _registrations.Add((type, name));
        _byType.Add(type, name);
        _byName.TryAdd(name.Value, type);
    }

    public MessageType GetName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _byType.TryGetValue(type, out var name) ? name : MessageTypeNaming.For(type);
    }

    public MessageTypeLookup Lookup(MessageType name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name.Value, out var clrType)
            ? new KnownMessageType(clrType)
            : new UnknownMessageType(name);
    }
}
