namespace Cabbie.Application.Cascades;

/// <summary>
/// Receives outgoing message bodies after a handler succeeds. Routing will send them.
/// Do not Invoke the next handler here.
/// </summary>
public interface ICascadePublisher
{
    void Publish(IReadOnlyList<object> outgoing);
}
