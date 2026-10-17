namespace Cabbie.Application.Transports;

/// <summary>
/// Two <see cref="ITransport"/> registrations claim the same URI scheme, so the router
/// cannot pick one. Names the scheme and both transport types.
/// </summary>
public sealed class DuplicateTransportScheme : InvalidOperationException
{
    public string Scheme { get; }

    public DuplicateTransportScheme(string scheme, IEnumerable<Type> transportTypes)
        : base(Describe(scheme, transportTypes))
    {
        Scheme = scheme;
    }

    private static string Describe(string scheme, IEnumerable<Type> transportTypes)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(transportTypes);
        return $"More than one transport is registered for scheme '{scheme}': {string.Join(", ", transportTypes.Select(t => t.FullName))}.";
    }
}
