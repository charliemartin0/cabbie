using Cabbie.Application.Bus;
using Cabbie.Application.Execution;
using Cabbie.Application.Persistence;
using Cabbie.Domain.Envelope;
using Cabbie.Tests.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cabbie.Tests.Infrastructure.Persistence;

/// <summary>
/// Prove-with for A5: the MoveToErrorQueue policy reaches the dead-letter store —
/// IErrorQueue.MoveAsync is wired to IDeadLetterStore.RecordAsync through the host's
/// DeadLetterQueueAdapter, carrying the inner exception as the cause.
/// </summary>
public sealed class DeadLetterQueueTests
{
    [Fact]
    public async Task move_to_error_queue_records_via_idl_store()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options =>
        {
            options.HandlerAssemblies.Add(typeof(FailsOnHandleMessage).Assembly);
            options.OnException<TimeoutException>().MoveToErrorQueue();
        });
        using IHost host = builder.Build();
        try
        {
            IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
            IDeadLetterStore store = host.Services.GetRequiredService<IDeadLetterStore>();

            HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
                () => bus.InvokeAsync(new FailsOnHandleMessage(7)));

            Assert.IsType<TimeoutException>(fault.InnerException);
            DeadLetter dead = Assert.Single(await store.ListAsync());
            Assert.IsType<FailsOnHandleMessage>(dead.Envelope.Message.Value);
            Assert.IsType<TimeoutException>(dead.Cause);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task failed_dead_letter_write_is_logged_and_surfaced()
    {
        var storeError = new InvalidOperationException("store is down");

        (HandlerFault fault, RecordingLoggerProvider logs) = await InvokeWithFailingStoreAsync(storeError);

        DeadLetterWriteFailed failed = Assert.IsType<DeadLetterWriteFailed>(fault.InnerException);
        Assert.Same(storeError, failed.InnerException);
        Assert.Equal("always fails", failed.OriginalFault.Message);
        Assert.Equal(1, logs.ExecutorErrors(storeError));
    }

    [Fact]
    public async Task dead_letter_store_cancellation_without_caller_cancellation_is_still_logged_and_surfaced()
    {
        // An HTTP or SDK timeout inside the store surfaces as TaskCanceledException even though
        // the caller never cancelled; the handler fault must not be lost.
        var storeError = new TaskCanceledException("store timed out");

        (HandlerFault fault, RecordingLoggerProvider logs) = await InvokeWithFailingStoreAsync(storeError);

        DeadLetterWriteFailed failed = Assert.IsType<DeadLetterWriteFailed>(fault.InnerException);
        Assert.Same(storeError, failed.InnerException);
        Assert.Equal(1, logs.ExecutorErrors(storeError));
    }

    private static async Task<(HandlerFault Fault, RecordingLoggerProvider Logs)> InvokeWithFailingStoreAsync(
        Exception storeError)
    {
        var logs = new RecordingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.UseCabbie(options =>
        {
            options.HandlerAssemblies.Add(typeof(FailsOnHandleMessage).Assembly);
            options.OnException<TimeoutException>().MoveToErrorQueue();
        });
        builder.Services.AddSingleton<IDeadLetterStore>(new ThrowingDeadLetterStore(storeError));
        using IHost host = builder.Build();
        try
        {
            IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

            HandlerFault fault = await Assert.ThrowsAsync<HandlerFault>(
                () => bus.InvokeAsync(new FailsOnHandleMessage(7)));
            return (fault, logs);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private sealed class ThrowingDeadLetterStore(Exception error) : IDeadLetterStore
    {
        public ValueTask RecordAsync(Envelope envelope, Exception cause, CancellationToken ct = default) =>
            throw error;

        public ValueTask<IReadOnlyList<DeadLetter>> ListAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<DeadLetter>>([]);
    }

    private sealed record LogEntry(string Category, LogLevel Level, EventId EventId, Exception? Exception);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public int ExecutorErrors(Exception exception) =>
            Entries.Count(entry =>
                entry.Category == typeof(Executor).FullName
                && entry.EventId.Id == 1
                && entry.Level == LogLevel.Error
                && ReferenceEquals(entry.Exception, exception));

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(RecordingLoggerProvider owner, string category) : ILogger
        {
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
                lock (owner._entries)
                {
                    owner._entries.Add(new LogEntry(category, logLevel, eventId, exception));
                }
            }
        }
    }
}