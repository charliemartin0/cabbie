namespace Cabbie.Application.Routing;

/// <summary>
/// Destination override on a message type: local://{queueName}/.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class LocalQueueAttribute : Attribute
{
    public string QueueName { get; }

    public LocalQueueAttribute(string queueName)
    {
        QueueName = queueName;
    }
}
