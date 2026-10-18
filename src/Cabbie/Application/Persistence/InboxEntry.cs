using Cabbie.Domain.Envelope.ValueObjects;

namespace Cabbie.Application.Persistence;

/// <summary>
/// One inbox row: an envelope id and the handler that acquired it.
/// </summary>
public sealed record InboxEntry(EnvelopeId Id, HandlerName Handler);
