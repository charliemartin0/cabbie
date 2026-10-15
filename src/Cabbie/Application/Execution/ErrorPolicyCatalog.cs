using Cabbie.Domain.Errors.ValueObjects;

namespace Cabbie.Application.Execution;

/// <summary>
/// Exception type (and optional message type) → chain of named recovery actions. A table of rules, not a loop.
/// </summary>
public sealed class ErrorPolicyCatalog
{
    private readonly Dictionary<(Type ExceptionType, Type? MessageType), List<ErrorAction>> _policies = [];

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

    public ErrorPolicyLookup For(Type exceptionType)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        return Lookup(exceptionType, messageType: null);
    }

    public ErrorPolicyLookup For(Type exceptionType, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        ArgumentNullException.ThrowIfNull(messageType);
        ErrorPolicyLookup specific = Lookup(exceptionType, messageType);
        if (specific is FoundErrorPolicy)
        {
            return specific;
        }

        return Lookup(exceptionType, messageType: null);
    }

    private ErrorPolicyLookup Lookup(Type exceptionType, Type? messageType)
    {
        if (_policies.TryGetValue((exceptionType, messageType), out List<ErrorAction>? actions))
        {
            return new FoundErrorPolicy([.. actions]);
        }

        return new MissingErrorPolicy(exceptionType);
    }
}
