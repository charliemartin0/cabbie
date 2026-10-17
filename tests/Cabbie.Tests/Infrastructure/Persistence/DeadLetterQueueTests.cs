using Cabbie.Application.Bus;
using Cabbie.Application.Execution;
using Cabbie.Application.Persistence;
using Cabbie.Tests.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Tests.Infrastructure.Persistence;

/// <summary>
/// Prove-with for A5: the MoveToErrorQueue policy reaches the dead-letter store —
/// IErrorQueue.Move is wired to IDeadLetterStore.RecordAsync through the host's
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
}