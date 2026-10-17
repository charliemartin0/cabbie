using Cabbie.Application.Tracking;

namespace Cabbie.Application.Bus;

/// <summary>
/// Public facade handlers and the sample host talk to. Invoke waits for the handler;
/// Publish accepts and lets go. Tracked Invoke/Publish drain immediate cascades for tests.
/// PlayDue lives on IMessageScheduler.
/// </summary>
public interface IMessageBus
{
    Task InvokeAsync(object message, CancellationToken cancellationToken = default);

    Task InvokeAsync(object message, DeliveryOptions options, CancellationToken cancellationToken = default);

    Task<TResult> InvokeAsync<TResult>(object message, CancellationToken cancellationToken = default);

    Task PublishAsync(object message, CancellationToken cancellationToken = default);

    Task PublishAsync(object message, DeliveryOptions options, CancellationToken cancellationToken = default);

    Task<TrackedSession> InvokeTrackedAsync(object message, CancellationToken cancellationToken = default);

    Task<TrackedSession> InvokeTrackedAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellationToken = default);

    Task<TrackedSession> PublishTrackedAsync(object message, CancellationToken cancellationToken = default);

    Task<TrackedSession> PublishTrackedAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellationToken = default);
}
