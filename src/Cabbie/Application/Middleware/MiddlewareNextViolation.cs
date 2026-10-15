namespace Cabbie.Application.Middleware;

/// <summary>
/// Wrapper returned without calling next exactly once, or called next twice.
/// Not retried and not mapped through error-policy actions.
/// </summary>
public sealed class MiddlewareNextViolation : Exception
{
    public MiddlewareNextViolation()
        : base("Middleware must call next exactly once.")
    {
    }
}
