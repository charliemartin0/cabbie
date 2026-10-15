namespace Cabbie.Tests.Handlers;

/// <summary>
/// A plain user message. No Cabbie attributes: routing comes from the fluent
/// options, discovery from the Handle convention.
/// </summary>
public sealed record ChargePayment(int OrderId);

/// <summary>
/// Records that the handler ran. Used by UseCabbieTests to prove the host
/// dispatched through the "payments" local queue.
/// </summary>
public sealed class ChargePaymentHandler
{
    private static TaskCompletionSource<ChargePayment> _handled = New();

    public static Task<ChargePayment> Handled => _handled.Task;

    public static void Reset() => _handled = New();

    public void Handle(ChargePayment message) => _handled.TrySetResult(message);

    private static TaskCompletionSource<ChargePayment> New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}