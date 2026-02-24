using System.Reflection;

namespace Egil.Orleans.StateMigration;

internal static class StateTypeIdentity
{
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
}
