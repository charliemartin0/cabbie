namespace Cabbie.Domain.Errors.ValueObjects;

/// <summary>
/// A chain of named actions for this exception (and optional message type).
/// </summary>
public sealed record FoundErrorPolicy(IReadOnlyList<ErrorAction> Actions) : ErrorPolicyLookup;
