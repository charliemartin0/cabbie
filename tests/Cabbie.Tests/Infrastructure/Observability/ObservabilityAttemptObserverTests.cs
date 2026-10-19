using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Infrastructure.Observability;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Infrastructure.Observability;

public sealed class ObservabilityAttemptObserverTests : IDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<Activity> _activities = new();
    private readonly List<long> _failureReadings = [];
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;

    public ObservabilityAttemptObserverTests()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CabbieDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = _activities.Enqueue,
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CabbieDiagnostics.MeterName
                    && instrument.Name == "cabbie.failures")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "cabbie.failures")
            {
                _failureReadings.Add(value);
            }
        });
        _meterListener.Start();
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    [Fact]
    public void failing_attempt_emits_an_activity_with_error_status_and_increments_failures_counter()
    {
        var observer = new ObservabilityAttemptObserver();
        Envelope envelope = ChargePaymentEnvelope();
        DiscoveredHandler handler = HandlerFor<AlwaysThrowHandler>();
        var exception = new TimeoutException("payment gateway timeout");

        observer.OnAttempt(envelope, handler, exception);

        Activity activity = Assert.Single(_activities, a => (string?)a.GetTagItem("cabbie.envelope.id") == envelope.Id.Value.ToString());
        Assert.Equal("Cabbie.Handler.Attempt", activity.OperationName);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Contains("TimeoutException", activity.StatusDescription ?? string.Empty);

        long value = Assert.Single(_failureReadings);
        Assert.Equal(1L, value);
    }

    [Fact]
    public void successful_attempt_emits_an_activity_without_error_status_and_does_not_increment_failures_counter()
    {
        var observer = new ObservabilityAttemptObserver();
        Envelope envelope = ChargePaymentEnvelope();
        DiscoveredHandler handler = HandlerFor<SucceedingHandler>();

        observer.OnAttempt(envelope, handler, exception: null);

        Activity activity = Assert.Single(_activities, a => (string?)a.GetTagItem("cabbie.envelope.id") == envelope.Id.Value.ToString());
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
        Assert.Empty(_failureReadings);
    }

    [Fact]
    public void activity_carries_message_type_destination_attempt_and_envelope_id_as_tags()
    {
        var observer = new ObservabilityAttemptObserver();
        Envelope envelope = ChargePaymentEnvelope();
        DiscoveredHandler handler = HandlerFor<SucceedingHandler>();

        observer.OnAttempt(envelope, handler, exception: null);

        Activity activity = Assert.Single(_activities, a => (string?)a.GetTagItem("cabbie.envelope.id") == envelope.Id.Value.ToString());
        Assert.Equal(typeof(ChargePayment).FullName, activity.GetTagItem("cabbie.message.type"));
        Assert.Equal(envelope.Destination.Value.ToString(), activity.GetTagItem("cabbie.destination")?.ToString());
        Assert.Equal(envelope.Attempts.Value, activity.GetTagItem("cabbie.attempt"));
        Assert.Equal(envelope.Id.Value.ToString(), activity.GetTagItem("cabbie.envelope.id")?.ToString());
    }

    private static Envelope ChargePaymentEnvelope() =>
        EnvelopeFactory.Create(
            message: new Message(new ChargePayment(1)),
            messageType: MessageTypeNaming.For(typeof(ChargePayment)));

    private static DiscoveredHandler HandlerFor<THandler>()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(THandler));
        return Assert.Single(catalog.Handlers);
    }

    public sealed class AlwaysThrowHandler
    {
        public void Handle(ChargePayment payment) => throw new TimeoutException("payment gateway timeout");
    }

    public sealed class SucceedingHandler
    {
        public ChargePayment Handle(ChargePayment payment) => payment;
    }
}
