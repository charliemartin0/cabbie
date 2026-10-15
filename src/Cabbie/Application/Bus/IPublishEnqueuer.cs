using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Bus;

/// <summary>
/// Receives a fully built envelope for a destination and hands it to the transport
/// that owns that destination. Local queues implement this; brokers will later.
/// </summary>
public interface IPublishEnqueuer
{
    void Enqueue(Envelope envelope);
}
