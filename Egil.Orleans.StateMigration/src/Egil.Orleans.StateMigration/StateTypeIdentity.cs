using System.Collections.Concurrent;
using System.Reflection;

namespace Egil.Orleans.StateMigration;

internal static class StateTypeIdentity
{
    private static readonly ConcurrentDictionary<string, Type> TypesByIdentity = new(StringComparer.Ordinal);

    public static string GetIdentity(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        string? alias = type.GetCustomAttribute<global::Orleans.AliasAttribute>(inherit: false)?.Alias;
        if (!string.IsNullOrWhiteSpace(alias))
        {
            return alias;
        }

        return type.FullName
               ?? type.AssemblyQualifiedName
               ?? throw new InvalidOperationException($"Unable to determine a stable identity for '{type}'.");
    }

    public static void RegisterRange(IEnumerable<Type> types)
    {
        foreach (Type type in types)
        {
            Register(type);
        }
    }

    public static bool TryResolve(string identity, [NotNullWhen(true)] out Type? type)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            type = null;
            return false;
        }

        if (TypesByIdentity.TryGetValue(identity, out type))
        {
            return true;
        }

        Type? found = null;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (Type candidate in GetLoadableTypes(assembly))
            {
                if (!string.Equals(GetIdentity(candidate), identity, StringComparison.Ordinal))
                {
                    continue;
                }

                if (found is not null && found != candidate)
                {
                    throw new InvalidOperationException(
                        $"Multiple types resolve to the same state identity '{identity}'.");
                }

                found = candidate;
            }
        }

        if (found is null)
        {
            type = null;
            return false;
        }

        TypesByIdentity.TryAdd(identity, found);
        type = found;
        return true;
    }

    private static void Register(Type type)
    {
        string identity = GetIdentity(type);
        if (TypesByIdentity.TryGetValue(identity, out Type? existingType) && existingType != type)
        {
            throw new InvalidOperationException(
                $"Multiple types resolve to the same state identity '{identity}'.");
        }

        TypesByIdentity.TryAdd(identity, type);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(static type => type is not null)!;
        }
    }
}
