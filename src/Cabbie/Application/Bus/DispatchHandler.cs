using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Bus;

/// <summary>
/// Dispatch seam shared by the scheduler, the local queues, and the local transport:
/// run every handler for an envelope's message type through the configured dispatch
/// (the Mediator's saga-aware path when hosted, MessageDelivery for direct construction).
/// </summary>
internal delegate Task DispatchHandler(Envelope envelope, CancellationToken cancellationToken);