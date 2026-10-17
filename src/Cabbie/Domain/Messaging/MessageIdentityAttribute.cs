namespace Cabbie.Domain.Messaging;

/// <summary>
/// Optional wire-name override for a CLR message type. When absent, MessageTypeNaming uses FullName.
/// </summary>

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class MessageIdentityAttribute : Attribute
{
    public string Alias { get; }

    public MessageIdentityAttribute(string alias)
    {
        Alias = alias;
    }
}