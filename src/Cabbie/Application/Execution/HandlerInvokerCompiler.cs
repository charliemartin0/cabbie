using System.Linq.Expressions;
using System.Reflection;
using Cabbie.Application.Discovery;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Execution;

/// <summary>
/// Builds a <see cref="HandlerInvoker"/> per handler at Scan time. The instance
/// construction (parameterless ctor, used when no ResolveTarget supplied the
/// instance), the argument binding and the method call are all baked in — one
/// compiled call site instead of Activator.CreateInstance + MethodInfo.Invoke +
/// an arguments array per attempt.
/// </summary>
internal static class HandlerInvokerCompiler
{
    public static HandlerInvoker Compile(DiscoveredHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        MethodInfo method = handler.Method;
        Type handlerType = handler.HandlerType;

        ParameterExpression target = Expression.Parameter(typeof(object), "target");
        ParameterExpression message = Expression.Parameter(typeof(object), "message");
        ParameterExpression envelope = Expression.Parameter(typeof(Envelope), "envelope");
        ParameterExpression cancellationToken = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        Expression instance;
        if (method.IsStatic)
        {
            instance = null!;
        }
        else
        {
            ConstructorInfo constructor = handlerType.GetConstructor(Type.EmptyTypes)
                ?? throw new InvalidOperationException(
                    $"Handler '{handlerType}' has no parameterless constructor; a compiled invoker cannot be built.");
            instance = Expression.Coalesce(
                Expression.Convert(target, handlerType),
                Expression.New(constructor));
        }

        var arguments = new List<Expression>
        {
            Expression.Convert(message, method.GetParameters()[0].ParameterType)
        };
        foreach (ParameterInfo slot in handler.InjectionSlots)
        {
            arguments.Add(slot.ParameterType == typeof(Envelope)
                ? envelope
                : cancellationToken);
        }

        Expression call = method.IsStatic
            ? Expression.Call(method, arguments)
            : Expression.Call(instance, method, arguments);

        Expression body = method.ReturnType == typeof(void)
            ? Expression.Block(call, Expression.Constant(null, typeof(object)))
            : Expression.Convert(call, typeof(object));

        return Expression.Lambda<HandlerInvoker>(body, target, message, envelope, cancellationToken)
            .Compile();
    }
}