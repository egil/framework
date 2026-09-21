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
/// always available where the scope is needed. A cached scope is only trusted while its resolver
/// is still reachable from the options' resolver, so replacing the resolver after registration
/// makes the options unregistered again.
/// </remarks>
internal sealed class MigrationScope
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, MigrationScope> Scopes = new();

    private readonly HashSet<Type> excludedTypes;
    private bool? usesReflectionFallback;

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
    /// Whether the migration resolver stands in for <see cref="DefaultJsonTypeInfoResolver"/>.
    /// STJ populates the default resolver only when the chain is empty at freeze time, and
    /// registering migration makes it non-empty, so users who configured no resolver would lose
    /// reflection-based serialization. The stand-in applies only while every resolver the root
    /// options delegate to is the migration resolver itself (possibly decorated); any other
    /// resolver, visible or hidden inside a decorator, is left to serve the remaining types as it
    /// would without migration. An application-defined decorator is opaque and counts as another
    /// resolver, so a caller using one names the resolver they want explicitly.
    /// </summary>
    public bool UsesReflectionFallback
    {
        get
        {
            if (usesReflectionFallback is { } cached)
            {
                return cached;
            }

            bool onlyMigration = false;
            foreach (IJsonTypeInfoResolver leaf in ResolverLeaves.Of(RootOptions.TypeInfoResolver))
            {
                if (leaf is not JsonMigrationTypeInfoResolver)
                {
                    onlyMigration = false;
                    break;
                }

                onlyMigration = true;
            }

            // Only cached once the options are frozen; before that the chain can still change.
            if (RootOptions.IsReadOnly)
            {
                usesReflectionFallback = onlyMigration;
            }

            return onlyMigration;
        }
    }

    /// <summary>
    /// Whether <paramref name="type"/>'s own converter is being built through these options, so
    /// that the type resolves to its plain object contract.
    /// </summary>
    public bool IsBuildingConverterFor(Type type) => excludedTypes.Contains(type);

    public MigrationScope CreateExcluding(Type type)
        => new(Resolver, RootOptions, new HashSet<Type>(excludedTypes) { type });

    public static void Register(JsonSerializerOptions options, MigrationScope scope)
        => Scopes.AddOrUpdate(options, scope);

    /// <summary>
    /// The scope registered for <paramref name="options"/>, provided its resolver is still reachable
    /// from the options' resolver; a stale entry (the resolver was replaced or the chain cleared) is
    /// dropped so the options count as unregistered.
    /// </summary>
    public static MigrationScope? Find(JsonSerializerOptions options)
    {
        if (!Scopes.TryGetValue(options, out MigrationScope? scope))
        {
            return null;
        }

        if (ResolverLeaves.Contains(options.TypeInfoResolver, scope.Resolver))
        {
            return scope;
        }

        Scopes.Remove(options);
        return null;
    }

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

        foreach (IJsonTypeInfoResolver leaf in ResolverLeaves.Of(options.TypeInfoResolver))
        {
            if (leaf is JsonMigrationTypeInfoResolver resolver)
            {
                var discovered = new MigrationScope(resolver, options, []);
                Register(options, discovered);
                return discovered;
            }
        }

        return null;
    }
}
