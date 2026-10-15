namespace Cabbie.Domain.Envelope.ValueObjects;

/// <summary>
/// Envelope.Data. Raw serialized body. Empty until Serialization has run.
/// </summary>
public record EnvelopeData
{
    public ReadOnlyMemory<byte> Value { get; }

    public EnvelopeData() : this(ReadOnlyMemory<byte>.Empty)
    {
    }

    public EnvelopeData(byte[]? value)
        : this(value is { Length: > 0 } ? value.AsMemory() : ReadOnlyMemory<byte>.Empty)
    {
    }

    public EnvelopeData(ReadOnlyMemory<byte> value)
    {
        Value = value.Length == 0 ? ReadOnlyMemory<byte>.Empty : value.ToArray();
    }

    public virtual bool Equals(EnvelopeData? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Value.Span.SequenceEqual(other.Value.Span);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var b in Value.Span)
        {
            hash.Add(b);
        }

        return hash.ToHashCode();
    }
}
