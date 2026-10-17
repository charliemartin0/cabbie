using System.Linq.Expressions;
using System.Reflection;
using Cabbie.Application.Discovery;
using Cabbie.Domain.Envelope;

namespace Cabbie.Application.Execution;

/// <summary>
/// Builds a <see cref="HandlerInvoker"/> per handler at Scan time. The instance cast
/// (the Executor resolves the target from the message scope), the argument binding
/// — message, Envelope, CancellationToken, scoped services — and the method call are
/// all baked in: one compiled call site instead of MethodInfo.Invoke + an arguments
/// array per attempt.
/// </summary>
internal static class HandlerInvokerCompiler
{
    private static readonly MethodInfo ResolveMethod = typeof(HandlerServices).GetMethod(
        nameof(HandlerServices.Resolve),
        BindingFlags.Public | BindingFlags.Static)!;

    public static HandlerInvoker Compile(DiscoveredHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        MethodInfo method = handler.Method;
        Type handlerType = handler.HandlerType;

        ParameterExpression target = Expression.Parameter(typeof(object), "target");
        ParameterExpression message = Expression.Parameter(typeof(object), "message");
        ParameterExpression envelope = Expression.Parameter(typeof(Envelope), "envelope");
        ParameterExpression cancellationToken = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        ParameterExpression services = Expression.Parameter(typeof(IServiceProvider), "services");

        Expression instance = method.IsStatic
            ? null!
            : Expression.Convert(target, handlerType);

        var arguments = new List<Expression>
        {
            Expression.Convert(message, method.GetParameters()[0].ParameterType)
        };
        foreach (ParameterInfo slot in handler.InjectionSlots)
        {
            arguments.Add(
                slot.ParameterType == typeof(Envelope) ? envelope
                : slot.ParameterType == typeof(CancellationToken) ? cancellationToken
                : Expression.Convert(
                    Expression.Call(ResolveMethod, services, Expression.Constant(slot, typeof(ParameterInfo))),
                    slot.ParameterType));
        }

        Expression call = method.IsStatic
            ? Expression.Call(method, arguments)
            : Expression.Call(instance, method, arguments);

        Expression body = method.ReturnType == typeof(void)
            ? Expression.Block(call, Expression.Constant(null, typeof(object)))
            : Expression.Convert(call, typeof(object));

        return Expression.Lambda<HandlerInvoker>(body, target, message, envelope, cancellationToken, services)
            .Compile();
    }
}