namespace Cabbie.Application.Transports;

/// <summary>
/// An envelope as it crosses a wire transport: string headers (the reserved <c>cabbie-*</c>
/// envelope fields plus the envelope's own custom headers) and the serialized body.
/// Produced and read by <see cref="EnvelopeCodec"/>; a transport maps it onto its native
/// message attributes and payload.
/// </summary>
public sealed record TransportMessage(IReadOnlyDictionary<string, string> Headers, ReadOnlyMemory<byte> Body);
