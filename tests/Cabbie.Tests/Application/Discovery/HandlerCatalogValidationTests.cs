using FluentValidation;
using Microsoft.Extensions.Hosting;
using Cabbie.Tests.InvalidCatalog;

namespace Cabbie.Tests.Application.Discovery;

/// <summary>
/// Prove-with for A6: the HandlerCatalogValidator runs when Cabbie is added (right after the
/// scan), so an invalid catalog fails fast instead of silently registering.
/// </summary>
public sealed class HandlerCatalogValidationTests
{
    [Fact]
    public void adding_cabbie_runs_handler_catalog_validator_after_scan()
    {
        var builder = Host.CreateApplicationBuilder();

        Assert.Throws<ValidationException>(
            () => builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(EmptyAliasScanMessage).Assembly)));
    }
}