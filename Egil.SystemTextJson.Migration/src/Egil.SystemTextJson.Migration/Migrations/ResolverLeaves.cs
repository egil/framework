using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// Enumerates the resolvers a <see cref="JsonSerializerOptions.TypeInfoResolver"/> ultimately
/// delegates to, seeing through System.Text.Json's resolver chains and modifier decorators
/// without calling any of them.
/// </summary>
/// <remarks>
/// Discovery must not call <c>GetTypeInfo</c> on a user's resolver: a
/// <see cref="DefaultJsonTypeInfoResolver"/> freezes its <c>Modifiers</c> on the first call, which
/// would break a caller that adds a modifier after <c>AddJsonMigrationSupport()</c>. The chain type
/// is an <see cref="IList{T}"/> of its entries, and <c>WithAddedModifier</c> wraps a resolver in the
/// internal <c>JsonTypeInfoResolverWithAddedModifiers</c>, whose <c>_source</c> field holds the
/// wrapped resolver; that field is read by reflection. A resolver of any other type is a leaf,
/// including application-defined decorators, which the walk cannot see through.
/// </remarks>
internal static class ResolverLeaves
{
    // Resolved once; null when the runtime no longer has the type or field, in which case a
    // decorated entry is reported as an opaque leaf rather than failing.
    // Unlike Assembly.GetType, the constant assembly-qualified Type.GetType lookup lets the
    // trimmer identify the type and preserve the field accessed below without requiring IL2026.
    private static readonly FieldInfo? DecoratorSourceField = Type
        .GetType("System.Text.Json.Serialization.Metadata.JsonTypeInfoResolverWithAddedModifiers, System.Text.Json")?
        .GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance);

    public static IEnumerable<IJsonTypeInfoResolver> Of(IJsonTypeInfoResolver? resolver)
    {
        var leaves = new List<IJsonTypeInfoResolver>();
        Collect(resolver, leaves, new HashSet<IJsonTypeInfoResolver>(ReferenceEqualityComparer.Instance));
        return leaves;
    }

    private static void Collect(IJsonTypeInfoResolver? resolver, List<IJsonTypeInfoResolver> leaves, HashSet<IJsonTypeInfoResolver> visited)
    {
        // An options-bound chain that was wrapped and assigned back to the same options contains
        // its own wrapper; the visited set stops that cycle instead of recursing forever.
        if (resolver is null || !visited.Add(resolver))
        {
            return;
        }

        switch (resolver)
        {
            case PlainContractResolver plain:
                Collect(plain.Inner, leaves, visited);
                return;
            case IEnumerable<IJsonTypeInfoResolver> chain:
                foreach (IJsonTypeInfoResolver entry in chain)
                {
                    Collect(entry, leaves, visited);
                }

                return;
        }

        if (DecoratorSourceField is not null && resolver.GetType() == DecoratorSourceField.DeclaringType)
        {
            Collect(DecoratorSourceField.GetValue(resolver) as IJsonTypeInfoResolver, leaves, visited);
            return;
        }

        leaves.Add(resolver);
    }
}
