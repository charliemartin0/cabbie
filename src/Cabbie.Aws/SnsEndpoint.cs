using System.Text.RegularExpressions;

namespace Cabbie.Aws;

/// <summary>
/// An <c>sns://topic-name/</c> address. The topic name is the URI host, so it is lowercase
/// (<see cref="Uri"/> lowercases hosts).
/// </summary>
internal sealed partial record SnsEndpoint(string TopicName)
{
    public const string Scheme = "sns";

    public static SnsEndpoint Parse(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri
            || !string.Equals(address.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{address}' is not an sns:// address.", nameof(address));
        }

        if (!TopicNamePattern().IsMatch(address.Host))
        {
            throw new ArgumentException(
                $"'{address}' does not name a topic: use 1-70 lowercase letters, digits or hyphens as the host.",
                nameof(address));
        }

        if (address.AbsolutePath is not ("" or "/") || address.Query.Length > 0 || address.Fragment.Length > 0)
        {
            throw new ArgumentException($"'{address}' must be of the form sns://topic-name/ (no path, query or fragment).", nameof(address));
        }

        return new SnsEndpoint(address.Host);
    }

    [GeneratedRegex("^[a-z0-9-]{1,70}$")]
    private static partial Regex TopicNamePattern();
}
