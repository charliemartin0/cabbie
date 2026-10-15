using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Cabbie.Application.Discovery;
using Cabbie.Tests.InvalidCatalog;

namespace Cabbie.Tests.Application.Discovery;

/// <summary>
/// Prove-with for A6: the HandlerCatalogValidator runs at host start (first catalog
/// resolution), so an invalid catalog fails fast instead of silently registering.
/// </summary>
public sealed class HandlerCatalogValidationTests
{
    [Fact]
    public void host_start_runs_handler_catalog_validator_after_scan()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(EmptyAliasScanMessage).Assembly));
        using IHost host = builder.Build();

        Assert.Throws<ValidationException>(
            () => host.Services.GetRequiredService<HandlerCatalog>());
    }
}