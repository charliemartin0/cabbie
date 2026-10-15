namespace Cabbie.Application.Middleware;

/// <summary>
/// Outer runs once around the retry loop. Inner runs once per attempt around Handle.
/// Registration names the layer; there is no default.
/// </summary>
public enum MiddlewareLayer
{
    Outer,
    Inner
}
