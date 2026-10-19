using System.Diagnostics;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Infrastructure.Observability;
using Cabbie.Tests.Domain;
using Cabbie.Tests.Domain.Envelope;
using Microsoft.Extensions.Logging;

namespace Cabbie.Tests.Infrastructure.Observability;

public sealed class AttemptCorrelationTests : IDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<Activity> _activities = new();
    private readonly ActivityListener _listener;

    public AttemptCorrelationTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CabbieDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = _activities.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void attempt_activity_carries_causation_correlation_and_conversation_ids()
    {
        Envelope envelope = ChargeEnvelope(new CausationId(Guid.NewGuid()));

        new ObservabilityAttemptObserver().OnAttempt(envelope, Handler(), exception: null);

        Activity activity = Assert.Single(_activities, a => (string?)a.GetTagItem("cabbie.envelope.id") == envelope.Id.Value.ToString());
        Assert.Equal(envelope.CausationId!.Value.ToString(), activity.GetTagItem("cabbie.causation.id"));
        Assert.Equal(envelope.CorrelationId.Value.ToString(), activity.GetTagItem("cabbie.correlation.id"));
        Assert.Equal(envelope.ConversationId.Value.ToString(), activity.GetTagItem("cabbie.conversation.id"));
    }

    [Fact]
    public void attempt_activity_for_a_top_level_envelope_has_no_causation_tag()
    {
        Envelope envelope = ChargeEnvelope(causation: null);
        new ObservabilityAttemptObserver().OnAttempt(envelope, Handler(), exception: null);

        Activity activity = Assert.Single(_activities, a => (string?)a.GetTagItem("cabbie.envelope.id") == envelope.Id.Value.ToString());
        Assert.Null(activity.GetTagItem("cabbie.causation.id"));
        Assert.NotNull(activity.GetTagItem("cabbie.correlation.id"));
    }

    [Fact]
    public async Task each_handler_attempt_runs_inside_a_logging_scope_with_the_envelope_ids()
    {
        var logger = new ScopeRecordingLogger();
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>().Retry();
        var executor = new Executor(policies, logger: logger);
        Envelope envelope = ChargeEnvelope(new CausationId(Guid.NewGuid()));

        await executor.InvokeAsync(envelope, Handler());

        // Two attempts (one failure, one success): one scope each, both closed afterwards.
        Assert.Equal(2, logger.Scopes.Count);
        Assert.All(logger.Scopes, scope =>
        {
            Assert.Equal(envelope.Id.Value, scope.State["EnvelopeId"]);
            Assert.Equal(envelope.CorrelationId.Value, scope.State["CorrelationId"]);
            Assert.Equal(envelope.ConversationId.Value, scope.State["ConversationId"]);
            Assert.Equal(envelope.CausationId!.Value, scope.State["CausationId"]);
            Assert.True(scope.Disposed);
        });
    }

    private static Envelope ChargeEnvelope(CausationId? causation) =>
        EnvelopeFactory.Create(
            message: new Message(new ChargePayment(1)),
            messageType: MessageTypeNaming.For(typeof(ChargePayment)),
            causationId: causation);

    private static DiscoveredHandler Handler()
    {
        var catalog = new HandlerCatalog();
        catalog.Scan(typeof(FailsOnceHandler));
        return Assert.Single(catalog.Handlers);
    }

    private sealed class ScopeRecordingLogger : ILogger<Executor>
    {
        public List<RecordedScope> Scopes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            var state2 = ((IEnumerable<KeyValuePair<string, object>>)state!).ToDictionary(pair => pair.Key, pair => pair.Value);
            var scope = new RecordedScope(state2);
            Scopes.Add(scope);
            return scope;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class RecordedScope(Dictionary<string, object> state) : IDisposable
    {
        public Dictionary<string, object> State => state;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}

public sealed class FailsOnceHandler
{
    private static int _calls;

    public void Handle(ChargePayment message)
    {
        if (Interlocked.Increment(ref _calls) % 2 == 1)
        {
            throw new TimeoutException("first attempt");
        }
    }
}
