using Cabbie.Application.Bus;
using Cabbie.Application.Sagas;
using Cabbie.Application.Tracking;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Tests.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cabbie.Tests.Infrastructure.Hosting;

/// <summary>
/// End-to-end conversation through a real generic host: discovery, DI scopes, cascades,
/// sagas, scheduled messages and a clean stop.
/// </summary>
public sealed class HostConversationTests
{
    [Fact]
    public async Task tracked_start_cascades_to_the_cascaded_message()
    {
        using IHost host = await StartedHostAsync();
        try
        {
            IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

            TrackedSession session = await bus.InvokeTrackedAsync(NewOrder());

            Assert.Equal(
                [typeof(ConvPlaceOrder), typeof(ConvChargePayment)],
                session.Executed.Select(executed => executed.Message.GetType()));
            Assert.Equal(ConvStatus.Charged, LoadSaga(host).Status);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task external_event_completes_the_saga_started_by_the_first_message()
    {
        var logs = new RecordingLoggerProvider();
        using IHost host = await StartedHostAsync(builder => builder.Logging.AddProvider(logs));
        try
        {
            IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
            await bus.InvokeTrackedAsync(NewOrder());

            TrackedSession paid = await bus.InvokeTrackedAsync(new ConvPaymentCharged(1));

            Assert.Equal([typeof(ConvPaymentCharged)], paid.Executed.Select(executed => executed.Message.GetType()));
            Assert.Equal(ConvStatus.CompletedByPayment, LoadSaga(host).Status);
            Assert.Contains(logs.Messages, message => message.Contains("Order 1 completed by payment", StringComparison.Ordinal));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task scheduled_timeout_cancels_the_in_progress_saga()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using IHost host = await StartedHostAsync();
        try
        {
            IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
            TrackedSession placed = await bus.InvokeTrackedAsync(NewOrder(now.AddMinutes(1)));

            Assert.Equal(ConvStatus.Charged, LoadSaga(host).Status);

            TrackedSession timedOut = await placed.PlayScheduledMessagesAsync(now.AddMinutes(2));

            Assert.Equal([typeof(ConvOrderTimeout)], timedOut.Executed.Select(executed => executed.Message.GetType()));
            Assert.Equal(ConvStatus.CompletedByTimeout, LoadSaga(host).Status);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task handler_dependencies_resolve_from_di_and_host_stops_cleanly()
    {
        var logs = new RecordingLoggerProvider();
        using IHost host = await StartedHostAsync(builder => builder.Logging.AddProvider(logs));

        await host.Services.GetRequiredService<IMessageBus>().InvokeAsync(new ConvSendReceipt(1));
        await host.StopAsync();

        Assert.Contains(logs.Messages, message => message.Contains("Receipt sent for order 1", StringComparison.Ordinal));
    }

    private static async Task<IHost> StartedHostAsync(Action<HostApplicationBuilder>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        configure?.Invoke(builder);
        builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(ConvOrderSaga).Assembly));
        IHost host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static ConvOrderSaga LoadSaga(IHost host) =>
        Assert.IsType<ConvOrderSaga>(
            host.Services.GetRequiredService<ISagaStore>().Load(typeof(ConvOrderSaga), new SagaId("1")));

    private static ConvPlaceOrder NewOrder(DateTimeOffset? dueAt = null) =>
        new(1, dueAt ?? DateTimeOffset.UtcNow.AddMinutes(5));

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Recorder(_messages);

        public void Dispose()
        {
        }

        private sealed class Recorder(List<string> sink) : ILogger
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
                lock (sink)
                {
                    sink.Add(formatter(state, exception));
                }
            }
        }
    }
}
