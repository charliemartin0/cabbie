namespace Cabbie.Domain.Envelope.ValueObjects;

/// <summary>
/// Envelope.Headers. String dictionary for transport and tracing metadata, not the message body.
/// </summary>
public record Headers
{
    public IReadOnlyDictionary<string, string> Value { get; }

    public Headers() : this(new Dictionary<string, string>())
    {
    }

    public Headers(IReadOnlyDictionary<string, string> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = new Dictionary<string, string>(value, StringComparer.Ordinal);
    }

    public virtual bool Equals(Headers? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Value.Count != other.Value.Count)
        {
            return false;
        }

        foreach (var (key, val) in Value)
        {
            if (!other.Value.TryGetValue(key, out var otherVal) || otherVal != val)
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var (key, val) in Value.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(val, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
