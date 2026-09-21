using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Egil.SystemTextJson.Migration.Migrations;

/// <summary>
/// What migration knows about one <see cref="JsonSerializerOptions"/> instance: the resolver that
/// serves it, the options the user configured (the root of any exclusion clones) and the types
/// whose converter is being built through this instance and must therefore resolve to their plain
/// object contract.
/// </summary>
/// <remarks>
/// Scopes are keyed on the options instance rather than on the resolver's position in
/// <see cref="JsonSerializerOptions.TypeInfoResolverChain"/>, because a decorator such as
/// <c>WithAddedModifier</c> hides the resolver from the chain while still delegating to it.
/// STJ hands every <c>GetTypeInfo</c> call the options being resolved, so the instance is
/// always available where the scope is needed.
/// </remarks>
internal sealed class MigrationScope
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, MigrationScope> Scopes = new();

    private readonly HashSet<Type> excludedTypes;

    public MigrationScope(JsonMigrationTypeInfoResolver resolver, JsonSerializerOptions rootOptions, HashSet<Type> excludedTypes)
    {
        Resolver = resolver;
        RootOptions = rootOptions;
        this.excludedTypes = excludedTypes;
    }

    public JsonMigrationTypeInfoResolver Resolver { get; }

    public JsonMigrationRegistry Registry => Resolver.Registry;

    /// <summary>
    /// The options the user configured. Exclusion clones copy the user's resolver chain, so this is
    /// where the chain shape (for the reflection fallback) is read.
    /// </summary>
    public JsonSerializerOptions RootOptions { get; }

    /// <summary>
    /// Whether <paramref name="type"/>'s own converter is being built through these options, so
    /// that the type resolves to its plain object contract.
    /// </summary>
    public bool IsBuildingConverterFor(Type type) => excludedTypes.Contains(type);

    public MigrationScope CreateExcluding(Type type)
        => new(Resolver, RootOptions, new HashSet<Type>(excludedTypes) { type });

    public static void Register(JsonSerializerOptions options, MigrationScope scope)
        => Scopes.AddOrUpdate(options, scope);

    public static MigrationScope? Find(JsonSerializerOptions options)
        => Scopes.TryGetValue(options, out MigrationScope? scope) ? scope : null;

    /// <summary>
    /// Finds the scope for <paramref name="options"/>, or builds one for an options instance that
    /// was copied from a registered one (a copy carries the resolver chain but no scope entry).
    /// </summary>
    public static MigrationScope? FindOrDiscover(JsonSerializerOptions options)
    {
        if (Find(options) is { } scope)
        {
            return scope;
        }

        foreach (IJsonTypeInfoResolver resolver in options.TypeInfoResolverChain)
        {
            if (resolver is JsonMigrationTypeInfoResolver migrationResolver)
            {
                return new MigrationScope(migrationResolver, options, []);
            }
        }

        return null;
    }
}
