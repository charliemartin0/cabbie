using Cabbie.Application.Bus;
using Cabbie.Application.Discovery;
using Cabbie.Application.Execution;
using Cabbie.Domain.Envelope;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Tests.Application.Bus;

/// <summary>
/// Host-level prove-with for the shared dispatch wiring: Invoke and Publish must run
/// through the SAME error-policy catalog — the one registered in DI, not a private one
/// built inside the local queues.
/// </summary>
public sealed class MessageDeliveryTests
{
    [Fact]
    public async Task publish_async_uses_the_same_error_policy_catalog_as_invoke()
    {
        var policies = new ErrorPolicyCatalog();
        policies.OnException<TimeoutException>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(1))
            .Then
            .MoveToErrorQueue();
        var errorQueue = new RecordingErrorQueue();

        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie();
        builder.Services.AddSingleton(policies);
        builder.Services.AddSingleton<IErrorQueue>(errorQueue);
        using IHost host = builder.Build();
        HandlerCatalog catalog = host.Services.GetRequiredService<HandlerCatalog>();
        catalog.Scan(typeof(SamePolicyHandler));
        await host.StartAsync();
        try
        {
            IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

            // The catalog the shared Executor is built from — the DI-registered instance.
            ErrorPolicyCatalog registered = host.Services.GetRequiredService<ErrorPolicyCatalog>();
            Assert.Same(policies, registered);

            // Invoke: attempt 1 throws, the retry succeeds — only the custom policy can do this.
            SamePolicyHandler.Reset();
            await bus.InvokeAsync(new SamePolicyMessage(1, AlwaysThrows: false));
            Assert.Equal([1, 2], SamePolicyHandler.SeenAttempts);

            // Publish: always throws — retried, then moved to the error queue by that same policy.
            SamePolicyHandler.Reset();
            await bus.PublishAsync(new SamePolicyMessage(2, AlwaysThrows: true));
            Envelope moved = await errorQueue.Moved.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal([1, 2], SamePolicyHandler.SeenAttempts);
            Assert.Equal(2, moved.Attempts.Value);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private sealed class RecordingErrorQueue : IErrorQueue
    {
        private readonly TaskCompletionSource<Envelope> _moved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Envelope> Moved => _moved.Task;

        public void Move(Envelope envelope, Exception cause) => _moved.TrySetResult(envelope);
    }
}

public sealed record SamePolicyMessage(int OrderId, bool AlwaysThrows);

public sealed class SamePolicyHandler
{
    private static readonly object Gate = new();
    private static readonly List<int> Seen = [];

    public static IReadOnlyList<int> SeenAttempts
    {
        get
        {
            lock (Gate)
            {
                return [.. Seen];
            }
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Seen.Clear();
        }
    }

    public void Handle(SamePolicyMessage message, Envelope envelope)
    {
        lock (Gate)
        {
            Seen.Add(envelope.Attempts.Value);
        }

        if (message.AlwaysThrows || envelope.Attempts.Value == 1)
        {
            throw new TimeoutException("gateway timeout");
        }
    }
}