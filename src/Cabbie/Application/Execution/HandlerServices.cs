using System.Collections.Concurrent;
using System.Reflection;
using Cabbie.Application.Discovery;
using Microsoft.Extensions.DependencyInjection;

namespace Cabbie.Application.Execution;

/// <summary>
/// Container lookups for one handler attempt. Every call receives that attempt's scope
/// provider, so constructor and method services share the same scoped instances.
/// </summary>
internal static class HandlerServices
{
    /// <summary>
    /// One <see cref="ObjectFactory"/> per handler type that is not registered in the container,
    /// so ActivatorUtilities analyses the constructors once, not on every attempt.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, ObjectFactory> Factories = new();

    public static object Resolve(IServiceProvider services, ParameterInfo slot) =>
        services.GetService(slot.ParameterType) ?? throw new HandlerDependencyMissing(slot);

    /// <summary>
    /// ResolveTarget wins (saga paths). Otherwise the container, then a cached factory for
    /// handlers that were never registered. A constructor failure is reported as
    /// <see cref="HandlerDependencyMissing"/> only when the container says a constructor
    /// parameter cannot be resolved; any other exception, including one thrown by the
    /// constructor itself, propagates unchanged.
    /// </summary>
    public static object CreateTarget(IServiceProvider services, DiscoveredHandler handler)
    {
        if (handler.ResolveTarget is { } resolve)
        {
            return resolve(services)!;
        }

        Type handlerType = handler.HandlerType;
        try
        {
            return services.GetService(handlerType)
                ?? Factories.GetOrAdd(handlerType, static type => ActivatorUtilities.CreateFactory(type, Type.EmptyTypes))(
                    services,
                    null);
        }
        catch (InvalidOperationException ex) when (FindUnresolvable(services, handlerType) is not null)
        {
            throw new HandlerDependencyMissing(FindUnresolvable(services, handlerType)!, handlerType, ex);
        }
    }

    /// <summary>
    /// The first constructor parameter of <paramref name="type"/> the container cannot supply, or
    /// null when some public constructor is fully satisfiable (or the container cannot say).
    /// Judges by the best-matching (most parameters) constructor when none is satisfiable.
    /// </summary>
    public static ParameterInfo? FindUnresolvable(IServiceProvider services, Type type) =>
        services.GetService(typeof(IServiceProviderIsService)) is IServiceProviderIsService isService
            ? FindUnresolvable(isService, type)
            : null;

    public static ParameterInfo? FindUnresolvable(IServiceProviderIsService isService, Type type)
    {
        ParameterInfo? unresolved = null;
        int widest = -1;
        foreach (ConstructorInfo constructor in type.GetConstructors())
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            ParameterInfo? missing = parameters.FirstOrDefault(
                parameter => !parameter.HasDefaultValue && !isService.IsService(parameter.ParameterType));
            if (missing is null)
            {
                return null;
            }

            if (parameters.Length > widest)
            {
                widest = parameters.Length;
                unresolved = missing;
            }
        }

        return unresolved;
    }
}
