using System.Reflection;

namespace Cabbie.Tests.Application.Discovery;

internal static class DiscoveryMethods
{
    public static MethodInfo PublicOn<T>(string name) => PublicOn(typeof(T), name);

    public static MethodInfo PublicOn(Type type, string name) =>
        type.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        ?? throw new InvalidOperationException($"Public method {type.Name}.{name} not found.");

    public static MethodInfo NonPublicOn<T>(string name) =>
        typeof(T).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        ?? throw new InvalidOperationException($"Non-public method {typeof(T).Name}.{name} not found.");
}
