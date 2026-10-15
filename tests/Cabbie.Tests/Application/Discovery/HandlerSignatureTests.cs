using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Cabbie.Application.Discovery;
using Cabbie.Tests.InvalidHandlers;

namespace Cabbie.Tests.Application.Discovery;

/// <summary>
/// Prove-with for DX #2: handler methods with an injection slot that is neither
/// Envelope nor CancellationToken are rejected at Scan time, with a message that
/// names the offending parameter so a user can find it without grepping.
/// </summary>
public sealed class HandlerSignatureTests
{
    [Fact]
    public void handler_with_ilogger_parameter_is_rejected_at_scan()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(BadLoggerHandler).Assembly));
        using IHost host = builder.Build();

        InvalidHandlerSignature error = Assert.Throws<InvalidHandlerSignature>(
            () => host.Services.GetRequiredService<HandlerCatalog>());

        Assert.Equal(typeof(BadLoggerHandler), error.HandlerType);
        Assert.Contains("BadLoggerHandler.Handle", error.Message);
        Assert.Contains("is not Envelope or CancellationToken", error.Message);
        Assert.Contains("DI resolution is not registered", error.Message);
    }

    [Fact]
    public void unknown_handler_signature_lists_offending_parameter_name()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(BadLoggerHandler).Assembly));
        using IHost host = builder.Build();

        InvalidHandlerSignature error = Assert.Throws<InvalidHandlerSignature>(
            () => host.Services.GetRequiredService<HandlerCatalog>());

        Assert.Contains("parameter 'logger'", error.Message);
    }
}