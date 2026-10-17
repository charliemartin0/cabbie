using System.Reflection;
using Cabbie.Application.Execution;
using Cabbie.Application.Routing;

namespace Cabbie;

public sealed class CabbieOptions
{
    public ICollection<Assembly> HandlerAssemblies { get; } = new List<Assembly>();

    /// <summary>
    /// The routing catalog the host registers. Fluent routing registrations made in the
    /// configure callback land here, so the resolved RoutingCatalog is the configured one.
    /// </summary>
    internal RoutingCatalog Routing { get; } = new();

    /// <summary>
    /// The error-policy catalog the host registers. Fluent policy registrations made in the
    /// configure callback land here, so the resolved ErrorPolicyCatalog is the configured one.
    /// </summary>
    internal ErrorPolicyCatalog Policies { get; } = new();

    /// <summary>
    /// When enabled, every handler attempt runs inside an <c>IOutboxTransaction</c>: its
    /// cascades are staged instead of published immediately, and committed (pending) or
    /// rolled back with the handler outcome. Opt-in so existing hosts keep the
    /// immediate-publish path.
    /// <para>
    /// The middleware stages cascades on a per-handler <c>IOutboxTransaction</c> and
    /// commits them in a <c>finally</c> after the handler (and saga save) succeed. This
    /// slice ships the staging + commit only — a kernel-side relay that loads pending
    /// envelopes and dispatches them is not yet implemented. With the flag on and no
    /// relay, in-process cascades that would have been published immediately are parked
    /// in <c>IMessageStore.Outbox.LoadPendingAsync()</c> until restart. Turn this on only
    /// when a follow-up slice (or a downstream consumer of the outbox port) drains the
    /// pending list — not against Postgres or in production until a relay exists.
    /// </para>
    /// </summary>
    public bool EnableTransactionalOutbox { get; private set; }

    public PublishExpression PublishMessage<T>() => Routing.PublishMessage<T>();

    public OnExceptionExpression OnException<TException>()
        where TException : Exception =>
        Policies.OnException<TException>();

    /// <summary>
    /// Opt this host into the transactional outbox middleware.
    /// <para>
    /// This slice ships the staging + commit; the pending envelopes still need a relay to
    /// drain them (see <see cref="EnableTransactionalOutbox"/>). Do not enable against
    /// Postgres or in production until that relay exists.
    /// </para>
    /// </summary>
    public CabbieOptions UseTransactionalOutbox()
    {
        EnableTransactionalOutbox = true;
        return this;
    }
}