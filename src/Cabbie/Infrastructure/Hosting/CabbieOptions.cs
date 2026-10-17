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

    /// <summary>
    /// Endpoints this host listens to, in registration order. Started when the host starts and
    /// stopped when it stops.
    /// </summary>
    internal List<Uri> ListenEndpoints { get; } = [];

    /// <summary>
    /// Listen to an endpoint given as an absolute URI string, e.g. <c>local://orders</c>. The scheme
    /// picks the transport; a scheme with no registered transport fails host start.
    /// </summary>
    public CabbieOptions ListenTo(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return ListenTo(new Uri(uri, UriKind.Absolute));
    }

    public CabbieOptions ListenTo(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("Endpoint URI must be absolute, e.g. local://orders.", nameof(uri));
        }

        if (!ListenEndpoints.Contains(uri))
        {
            ListenEndpoints.Add(uri);
        }

        return this;
    }

    public OnExceptionExpression OnException<TException>()
        where TException : Exception =>
        Policies.OnException<TException>();

    /// <summary>
    /// Default policy for exceptions no <see cref="OnException{TException}"/> rule matches, on queue deliveries only
    /// (<c>InvokeAsync</c> never retries or dead-letters by default). The built-in default retries once per entry of
    /// <see cref="DefaultRetryDelays"/>, then dead-letters. The first call replaces it with an empty chain that you
    /// fill by chaining actions; later calls append to that chain. With no chained actions, unmatched exceptions fail
    /// fast. <see cref="DefaultRetryDelays"/> is ignored once this is called.
    /// </summary>
    public OnExceptionExpression OnUnmatchedException() => Policies.OnUnmatchedException();

    /// <summary>
    /// Cooldowns of the built-in default policy: one retry after each delay, in order, then dead-letter
    /// (default 100ms, 500ms, 2s). Empty means dead-letter on the first failure. Delays must not be negative.
    /// </summary>
    public IReadOnlyList<TimeSpan> DefaultRetryDelays { get; set; } = ErrorPolicyCatalog.BuiltInRetryDelays;

    /// <summary>
    /// Per-attempt handler timeout for every handler without a <see cref="HandlerTimeoutAttribute"/>.
    /// Null (the default) means no timeout. Must be greater than zero when set.
    /// </summary>
    public TimeSpan? HandlerTimeout { get; set; }

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