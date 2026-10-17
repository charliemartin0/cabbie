using System.Reflection;
using Cabbie.Application.Execution;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Sagas;

namespace Cabbie.Application.Discovery;

/// <summary>
/// Message type to handler method(s). Opt-in types and assemblies; include/exclude filters.
/// Missing is a lookup result, not an exception.
/// </summary>
public sealed class HandlerCatalog
{
    private static readonly BindingFlags HandlerMethods =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private readonly List<DiscoveredHandler> _handlers = [];
    private readonly HashSet<Type> _scannedTypes = [];
    private readonly HashSet<string> _includedNamespaces = new(StringComparer.Ordinal);
    private readonly HashSet<string> _excludedNamespaces = new(StringComparer.Ordinal);
    private readonly HashSet<Type> _excludedTypes = [];

    /// <summary>
    /// Message type → handlers, rebuilt at the end of every successful Scan so the hot
    /// lookup path is one dictionary hit instead of a filter over the handler list.
    /// </summary>
    internal Dictionary<Type, DiscoveredHandler[]> _byMessageType = [];

    public IReadOnlyList<DiscoveredHandler> Handlers => _handlers;

    public MessageTypeCatalog MessageTypes { get; } = new();

    public void Scan(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (Type type in assembly.GetTypes())
        {
            Scan(type);
        }
    }

    public void Scan(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        if (!_scannedTypes.Add(handlerType))
        {
            return;
        }

        if (handlerType.IsAbstract)
        {
            return;
        }

        if (!ShouldScan(handlerType))
        {
            return;
        }

        try
        {
            List<DiscoveredHandler> discovered = [];
            foreach (MethodInfo method in handlerType.GetMethods(HandlerMethods))
            {
                DiscoveredHandler? handler = HandlerConvention.For(method);
                if (handler is null)
                {
                    continue;
                }

                discovered.Add(handler with { CachedInvoker = HandlerInvokerCompiler.Compile(handler) });
            }

            if (IsSagaHandlerType(handlerType))
            {
                EnsureValidSagaHandlers(handlerType, discovered);
            }

            foreach (DiscoveredHandler handler in discovered)
            {
                _handlers.Add(handler);
                MessageTypes.Register(handler.MessageClrType);
            }

            _byMessageType = _handlers
                .GroupBy(handler => handler.MessageClrType)
                .ToDictionary(group => group.Key, group => group.ToArray());
        }
        catch
        {
            _scannedTypes.Remove(handlerType);
            throw;
        }
    }

    public void IncludeNamespace(string namespaceName)
    {
        ArgumentNullException.ThrowIfNull(namespaceName);
        _includedNamespaces.Add(namespaceName);
    }

    public void ExcludeNamespace(string namespaceName)
    {
        ArgumentNullException.ThrowIfNull(namespaceName);
        _excludedNamespaces.Add(namespaceName);
    }

    public void ExcludeType(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        _excludedTypes.Add(handlerType);
    }

    public HandlerLookup Lookup(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        return _byMessageType.TryGetValue(messageType, out DiscoveredHandler[]? found)
            ? new FoundHandlers(found)
            : new MissingHandler(messageType);
    }

    private bool ShouldScan(Type handlerType)
    {
        if (_excludedTypes.Contains(handlerType))
        {
            return false;
        }

        if (InAnyNamespace(handlerType.Namespace, _excludedNamespaces))
        {
            return false;
        }

        if (_includedNamespaces.Count == 0)
        {
            return true;
        }

        return InAnyNamespace(handlerType.Namespace, _includedNamespaces);
    }

    private static bool InAnyNamespace(string? ns, HashSet<string> prefixes)
    {
        if (ns is null)
        {
            return false;
        }

        foreach (string prefix in prefixes)
        {
            if (ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSagaHandlerType(Type handlerType) =>
        handlerType != typeof(Saga) && handlerType.IsAssignableTo(typeof(Saga));

    private static void EnsureValidSagaHandlers(Type handlerType, List<DiscoveredHandler> discovered)
    {
        if (handlerType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidHandlerSignature(
                handlerType,
                $"Saga '{handlerType}' needs a public parameterless constructor: saga state is created and loaded by Cabbie, not resolved from the container. Take services as handler method parameters.");
        }

        foreach (DiscoveredHandler handler in discovered)
        {
            if (handler.IsStatic)
            {
                throw new InvalidHandlerSignature(
                    handlerType,
                    $"Saga '{handlerType}' cannot use a static handler method.");
            }

            if (!SagaIdentityNaming.CanCorrelate(handler.MessageClrType, handlerType))
            {
                throw new InvalidHandlerSignature(
                    handlerType,
                    $"Saga '{handlerType}' message '{handler.MessageClrType}' is not correlatable.");
            }
        }

        foreach (IGrouping<Type, DiscoveredHandler> group in discovered.GroupBy(handler => handler.MessageClrType))
        {
            if (group.Count() > 1)
            {
                throw new InvalidHandlerSignature(
                    handlerType,
                    $"Saga '{handlerType}' cannot declare more than one catalog method for '{group.Key}'.");
            }
        }
    }
}
