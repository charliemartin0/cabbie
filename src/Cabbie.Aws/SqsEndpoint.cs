using System.Text.RegularExpressions;

namespace Cabbie.Aws;

/// <summary>
/// An <c>sqs://queue-name/</c> address. The queue name is the URI host, so it is lowercase
/// (<see cref="Uri"/> lowercases hosts). The dead-letter queue is <c>{name}-dead-letter</c>.
/// </summary>
internal sealed partial record SqsEndpoint(string QueueName)
{
    public const string Scheme = "sqs";

    private const string DeadLetterSuffix = "-dead-letter";

    public string DeadLetterQueueName => QueueName + DeadLetterSuffix;

    public static SqsEndpoint Parse(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri
            || !string.Equals(address.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{address}' is not an sqs:// address.", nameof(address));
        }

        if (!QueueNamePattern().IsMatch(address.Host))
        {
            throw new ArgumentException(
                $"'{address}' does not name a queue: use 1-70 lowercase letters, digits or hyphens as the host.",
                nameof(address));
        }

        if (address.AbsolutePath is not ("" or "/") || address.Query.Length > 0 || address.Fragment.Length > 0)
        {
            throw new ArgumentException($"'{address}' must be of the form sqs://queue-name/ (no path, query or fragment).", nameof(address));
        }

        return new SqsEndpoint(address.Host);
    }

    [GeneratedRegex("^[a-z0-9-]{1,70}$")]
    private static partial Regex QueueNamePattern();
}
