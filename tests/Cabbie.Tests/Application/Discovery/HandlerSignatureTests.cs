using Microsoft.Extensions.Hosting;
using Cabbie.Application.Discovery;
using Cabbie.Tests.InvalidHandlers;

namespace Cabbie.Tests.Application.Discovery;

/// <summary>
/// Prove-with for DX #2: a handler method parameter the container cannot fill (out, ref,
/// params) is rejected when Cabbie is added, with a message that names the offending
/// parameter so a user can find it without grepping.
/// </summary>
public sealed class HandlerSignatureTests
{
    [Fact]
    public void handler_with_by_ref_parameter_is_rejected_when_cabbie_is_added()
    {
        var builder = Host.CreateApplicationBuilder();

        InvalidHandlerSignature error = Assert.Throws<InvalidHandlerSignature>(
            () => builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(BadRefHandler).Assembly)));

        Assert.Equal(typeof(BadRefHandler), error.HandlerType);
        Assert.Contains("parameter 'count'", error.Message);
        Assert.Contains("out, ref, or params", error.Message);
    }
}
