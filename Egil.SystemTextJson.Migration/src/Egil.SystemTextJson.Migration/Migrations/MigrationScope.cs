using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// always available where the scope is needed. A scope found during resolution is honoured as
/// is, whichever migration resolver registered it: its exclusions and root options describe the
/// options instance, and a resolution in progress proves the options are still served by
/// migration. Structural validation, which sees through STJ's chains and decorators but not
/// through application-defined wrappers, is used only by the registration guard, where it must
/// detect a resolver that was replaced or cleared; it never runs while a resolution is in
/// progress, because dropping an exclusion scope then would let a type's converter build itself
/// again without end.
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

    /// <summary>
    /// The same exclusions and root options attributed to <paramref name="resolver"/>, for a
    /// resolver that builds a converter under a scope another migration resolver registered.
    /// </summary>
    public MigrationScope ForResolver(JsonMigrationTypeInfoResolver resolver)
        => ReferenceEquals(resolver, Resolver) ? this : new MigrationScope(resolver, RootOptions, excludedTypes);

    public static void Register(JsonSerializerOptions options, MigrationScope scope)
        => Scopes.AddOrUpdate(options, scope);

    /// <summary>
    /// The scope registered for <paramref name="options"/>, if any. Callers that are themselves
    /// the resolver compare <see cref="Resolver"/> with their own identity.
    /// </summary>
    public static MigrationScope? Find(JsonSerializerOptions options)
        => Scopes.TryGetValue(options, out MigrationScope? scope) ? scope : null;

    /// <summary>
    /// The scope to build union routing from: the cached scope while its resolver is still reachable
    /// through STJ's chains and decorators, otherwise one discovered from the current resolver, or
    /// <see langword="null"/> when the options no longer carry migration support. Nothing is
    /// removed, so this is safe during resolution; an application-defined wrapper is opaque to it.
    /// </summary>
    public static MigrationScope? FindReachable(JsonSerializerOptions options)
    {
        if (Find(options) is { } scope && ResolverLeaves.Contains(options.TypeInfoResolver, scope.Resolver))
        {
            return scope;
        }

        return Discover(options);
    }

    /// <summary>
    /// The registration to honour when <c>AddJsonMigrationSupport()</c> is called again: a cached
    /// scope while its resolver is still reachable through STJ's chains and decorators, otherwise
    /// one discovered from the current resolver. A cached scope whose resolver was replaced or
    /// cleared is dropped so the options count as unregistered, unless the chain holds a resolver
    /// the walk cannot see through; an application-defined wrapper around the migration entry
    /// looks exactly like that, and the cached registration, with the migrators the user
    /// configured, is then the one still serving the options. <paramref name="hidden"/> reports
    /// that case so the caller can put the resolver back where the walk sees it.
    /// </summary>
    public static MigrationScope? FindRegistration(JsonSerializerOptions options, out bool hidden)
    {
        hidden = false;

        if (Find(options) is { } scope)
        {
            if (ResolverLeaves.Contains(options.TypeInfoResolver, scope.Resolver))
            {
                return scope;
            }

            foreach (IJsonTypeInfoResolver leaf in ResolverLeaves.Of(options.TypeInfoResolver))
            {
                if (MayDelegateToMigration(leaf))
                {
                    hidden = true;
                    return scope;
                }
            }

            Scopes.Remove(options);
        }

        return Discover(options);
    }

    /// <summary>
    /// Whether <paramref name="leaf"/> may forward to a migration resolver the structural walk
    /// cannot see. STJ's own resolvers resolve contracts themselves, so only an application-defined
    /// resolver can; the walk already sees through STJ's decorator and chains.
    /// </summary>
    private static bool MayDelegateToMigration(IJsonTypeInfoResolver leaf)
        => leaf is not (JsonMigrationTypeInfoResolver or JsonSerializerContext)
            && leaf.GetType() != typeof(DefaultJsonTypeInfoResolver);

    private static MigrationScope? Discover(JsonSerializerOptions options)
    {
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
