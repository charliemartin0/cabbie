namespace Cabbie.Domain.Errors.ValueObjects;

/// <summary>
/// Result of looking up an error-policy chain. Missing is a handled result, not an exception.
/// </summary>
public abstract record ErrorPolicyLookup;
