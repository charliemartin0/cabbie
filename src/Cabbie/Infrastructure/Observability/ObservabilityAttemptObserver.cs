using System.Diagnostics;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;

namespace Cabbie.Infrastructure.Observability;

/// <summary>
/// <see cref="IHandlerAttemptObserver"/> that emits one <see cref="Activity"/> per handler
/// attempt and increments the failure counter when the attempt throws. A no-op exporter:
/// nothing is exported until an external listener subscribes via <see cref="CabbieDiagnostics"/>.
/// </summary>
public sealed class ObservabilityAttemptObserver : IHandlerAttemptObserver
{
    public void OnAttempt(Envelope envelope, DiscoveredHandler handler, Exception? exception)
    {
        using Activity? activity = CabbieDiagnostics.Source.StartActivity(
            "Cabbie.Handler.Attempt",
            ActivityKind.Internal);

        activity?.SetTag("cabbie.message.type", handler.MessageClrType.FullName);
        activity?.SetTag("cabbie.destination", envelope.Destination.Value);
        activity?.SetTag("cabbie.attempt", envelope.Attempts.Value);
        activity?.SetTag("cabbie.envelope.id", envelope.Id.Value.ToString());

        if (exception is null)
        {
            return;
        }

        activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        CabbieDiagnostics.Failures.Add(1);
    }
}
