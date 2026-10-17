using Cabbie.Tests.Domain;

namespace Cabbie.Tests.Application.Discovery.InvalidSignatures;

/// <summary>
/// A handler with an injection slot that is neither Envelope nor CancellationToken.
/// Scan rejects it; it lives in the InvalidSignatures namespace so assembly-wide
/// scans that exclude this namespace stay green.
/// </summary>
public interface IPaymentGateway;

public sealed class ChargePaymentHandler
{
    public Task HandleAsync(ChargePayment message, IPaymentGateway gateway, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}