namespace Cabbie.Application.Execution;

/// <summary>
/// The handler fault was due to be dead-lettered but the dead-letter write itself failed.
/// <see cref="Exception.InnerException"/> is the write failure; <see cref="OriginalFault"/> is the
/// original handler exception.
/// </summary>
public sealed class DeadLetterWriteFailed : Exception
{
    public DeadLetterWriteFailed(Guid envelopeId, Exception originalFault, Exception writeFailure)
        : base($"Dead-letter write failed for envelope {envelopeId}; the handler fault is in OriginalFault.", writeFailure)
    {
        ArgumentNullException.ThrowIfNull(originalFault);
        EnvelopeId = envelopeId;
        OriginalFault = originalFault;
    }

    public Guid EnvelopeId { get; }

    public Exception OriginalFault { get; }
}
