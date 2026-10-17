namespace Cabbie.Application.Transports;

/// <summary>
/// An endpoint URI uses a scheme no registered <see cref="ITransport"/> owns. Names the
/// scheme, the endpoint and the registered schemes so the missing registration or the
/// typo in the URI is found without a debugger. Raised when the host starts, or when a
/// send reaches an endpoint the startup check did not cover.
/// </summary>
public sealed class UnknownTransportScheme : InvalidOperationException
{
    public Uri Endpoint { get; }

    public string Scheme { get; }

    public UnknownTransportScheme(Uri endpoint, IReadOnlyCollection<string> registeredSchemes)
        : base(Describe(endpoint, registeredSchemes))
    {
        Endpoint = endpoint;
        Scheme = endpoint.Scheme;
    }

    private static string Describe(Uri endpoint, IReadOnlyCollection<string> registeredSchemes)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(registeredSchemes);
        string scheme = endpoint.Scheme;
        return $"No transport is registered for scheme '{scheme}' (endpoint '{endpoint}'). Registered schemes: {string.Join(", ", registeredSchemes.Order(StringComparer.Ordinal))}. Register an ITransport for '{scheme}' or fix the endpoint URI.";
    }
}
