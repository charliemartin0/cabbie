namespace Cabbie.Domain.Errors.ValueObjects;

/// <summary>
/// No policy was declared for this exception type.
/// </summary>
public sealed record MissingErrorPolicy(Type ExceptionType) : ErrorPolicyLookup;
