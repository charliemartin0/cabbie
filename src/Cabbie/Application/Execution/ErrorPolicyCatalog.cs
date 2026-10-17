using Cabbie.Domain.Errors.ValueObjects;

namespace Cabbie.Application.Execution;

/// <summary>
/// Exception type (and optional message type) → chain of named recovery actions. A table of rules, not a loop.
/// </summary>
public sealed class ErrorPolicyCatalog
{
    /// <summary>The built-in default retry delays: 100ms, 500ms, 2s, then dead-letter.</summary>
    public static IReadOnlyList<TimeSpan> BuiltInRetryDelays { get; } =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(2),
    ];

    private readonly Dictionary<(Type ExceptionType, Type? MessageType), List<ErrorAction>> _policies = [];

    private readonly List<ErrorAction> _default = BuildDefault(BuiltInRetryDelays);

    private bool _defaultReplaced;

    public OnExceptionExpression OnException<TException>()
        where TException : Exception =>
        OnException(typeof(TException));

    public OnExceptionExpression OnException<TException, TMessage>()
        where TException : Exception =>
        OnException(typeof(TException), typeof(TMessage));

    public OnExceptionExpression OnException(Type exceptionType)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        return new OnExceptionExpression(this, exceptionType);
    }

    public OnExceptionExpression OnException(Type exceptionType, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        ArgumentNullException.ThrowIfNull(messageType);
        return new OnExceptionExpression(this, exceptionType, messageType);
    }

    /// <summary>
    /// Chain for exceptions no rule matches on queue deliveries (never on <c>InvokeAsync</c>, which fails fast).
    /// The first call discards the built-in default (cooldown retries, then dead-letter) and starts an empty chain;
    /// every later call appends to that same chain, in call order. A first call with no chained actions leaves the
    /// chain empty, which means fail fast (<see cref="MissingErrorPolicy"/>).
    /// </summary>
    public OnExceptionExpression OnUnmatchedException()
    {
        if (!_defaultReplaced)
        {
            _default.Clear();
            _defaultReplaced = true;
        }

        return new OnExceptionExpression(this);
    }

    /// <summary>
    /// Sets the retry delays of the built-in default (one retry per delay, then dead-letter). An empty list
    /// dead-letters on the first failure. Ignored once <see cref="OnUnmatchedException"/> has replaced the default.
    /// </summary>
    internal void UseDefaultRetryDelays(IEnumerable<TimeSpan> delays)
    {
        ArgumentNullException.ThrowIfNull(delays);
        if (_defaultReplaced)
        {
            return;
        }

        TimeSpan[] copy = [.. delays];
        foreach (TimeSpan delay in copy)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero, nameof(delays));
        }

        _default.Clear();
        _default.AddRange(BuildDefault(copy));
    }

    internal void RegisterDefault(ErrorAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _default.Add(action);
    }

    public void Register(Type exceptionType, Type? messageType, ErrorAction action)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        ArgumentNullException.ThrowIfNull(action);
        var key = (exceptionType, messageType);
        if (!_policies.TryGetValue(key, out List<ErrorAction>? actions))
        {
            actions = [];
            _policies[key] = actions;
        }

        actions.Add(action);
    }

    /// <summary>
    /// Policy for <paramref name="exceptionType"/>: the closest type in its inheritance chain that has a rule wins,
    /// then (when <paramref name="useDefault"/>) the unmatched-exception default.
    /// </summary>
    public ErrorPolicyLookup For(Type exceptionType, bool useDefault = true)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        for (Type? type = exceptionType; type is not null && typeof(Exception).IsAssignableFrom(type); type = type.BaseType)
        {
            if (_policies.TryGetValue((type, null), out List<ErrorAction>? actions))
            {
                return new FoundErrorPolicy([.. actions]);
            }
        }

        return Default(exceptionType, useDefault);
    }

    /// <summary>
    /// Like <see cref="For(Type, bool)"/>, but at each inheritance level a rule scoped to <paramref name="messageType"/>
    /// beats the global rule for the same exception type. A closer global rule still beats a message-scoped rule
    /// on a base type.
    /// </summary>
    public ErrorPolicyLookup For(Type exceptionType, Type messageType, bool useDefault = true)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        ArgumentNullException.ThrowIfNull(messageType);
        for (Type? type = exceptionType; type is not null && typeof(Exception).IsAssignableFrom(type); type = type.BaseType)
        {
            if (_policies.TryGetValue((type, messageType), out List<ErrorAction>? scoped))
            {
                return new FoundErrorPolicy([.. scoped]);
            }

            if (_policies.TryGetValue((type, null), out List<ErrorAction>? global))
            {
                return new FoundErrorPolicy([.. global]);
            }
        }

        return Default(exceptionType, useDefault);
    }

    private static List<ErrorAction> BuildDefault(IEnumerable<TimeSpan> delays)
    {
        List<ErrorAction> chain = [.. delays.Select(delay => (ErrorAction)new RetryWithCooldown(delay))];
        chain.Add(new MoveToErrorQueue());
        return chain;
    }

    private ErrorPolicyLookup Default(Type exceptionType, bool useDefault) =>
        useDefault && _default.Count > 0 ? new FoundErrorPolicy([.. _default]) : new MissingErrorPolicy(exceptionType);
}
