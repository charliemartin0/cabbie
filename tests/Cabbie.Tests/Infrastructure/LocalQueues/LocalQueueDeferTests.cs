using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Infrastructure.LocalQueues;
using Cabbie.Tests.Application.Transports;
using Cabbie.Tests.Domain.Envelope;
using Microsoft.Extensions.Logging;

namespace Cabbie.Tests.Infrastructure.LocalQueues;

public sealed class LocalQueueDeferTests
{
    [Fact]
    public async Task deferred_envelope_that_cannot_go_back_on_a_draining_queue_is_logged_not_silently_dropped()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new RecordingReceiver(ReceiveOutcome.Defer) { Gate = gate };
        var logger = new CapturingLogger();
        var agent = new LocalQueueAgent("local://orders/", receiver, logger);
        Envelope envelope = EnvelopeFactory.Create(destination: new Destination(new Uri("local://orders/")));

        agent.Enqueue(envelope);
        await receiver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task draining = agent.DrainAsync(CancellationToken.None);
        gate.SetResult();
        await draining.WaitAsync(TimeSpan.FromSeconds(5));

        string message = Assert.Single(logger.Messages);
        Assert.Contains("local://orders/", message, StringComparison.Ordinal);
        Assert.Contains(envelope.Id.Value.ToString(), message, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger<LocalQueueAgent>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
