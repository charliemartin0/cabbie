using Cabbie.Application.Bus;
using Cabbie.Tests.Domain;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Tests.Application.Bus;

/// <summary>
/// Prove-with for A6: the EnvelopeValidator runs when minting a user-supplied envelope,
/// so an envelope that fails the domain rules throws instead of silently flowing on.
/// </summary>
public sealed class EnvelopeValidationTests
{
    [Fact]
    public async Task envelope_validator_runs_when_minting_an_envelope()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.UseCabbie();
        using IHost host = builder.Build();
        IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();

        // EmptyAliasMessage carries [MessageIdentity("")] — an empty wire name that
        // the MessageTypeValidator rejects, making the minted envelope invalid.
        await Assert.ThrowsAsync<ValidationException>(
            () => bus.PublishAsync(new EmptyAliasMessage()));
    }
}