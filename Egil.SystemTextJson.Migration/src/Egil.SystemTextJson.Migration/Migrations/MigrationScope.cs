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
/// always available where the scope is needed. A scope found during resolution is honoured as
/// is, whichever migration resolver registered it: its exclusions and root options describe the
/// options instance, and a resolution in progress proves the options are still served by
/// migration. Validation, which walks STJ's chains and decorators and probes application-defined
/// resolvers, is used by the registration guard, where it must detect a resolver that was
/// replaced or cleared, and by the union classifier; it never runs while a contract is being
/// resolved through the scope, because dropping an exclusion scope then would let a type's
/// converter build itself again without end.
/// </remarks>
internal sealed class MigrationScope
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, MigrationScope> Scopes = new();

    // The registration made by AddJsonMigrationSupport, keyed on the chain object the options
    // resolve through at that point. STJ's copy constructor hands a copy that same object as its
    // resolver until the copy changes its own chain, so a copy finds the registration here when
    // it carries none of its own; a copy that did change its chain is matched through the
    // entries the two chains share (see FindByLeaf).
    private static readonly ConditionalWeakTable<IJsonTypeInfoResolver, MigrationScope> ScopesByChain = new();

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
    /// Records the registration <c>AddJsonMigrationSupport()</c> made, under the options and under
    /// the chain object they now resolve through.
    /// </summary>
    public static void RegisterRoot(JsonSerializerOptions options, MigrationScope scope)
    {
        Register(options, scope);
        if (options.TypeInfoResolver is { } chain)
        {
            ScopesByChain.AddOrUpdate(chain, scope);
        }
    }

    /// <summary>
    /// The scope registered for <paramref name="options"/>, if any. Callers that are themselves
    /// the resolver compare <see cref="Resolver"/> with their own identity.
    /// </summary>
    public static MigrationScope? Find(JsonSerializerOptions options)
        => Scopes.TryGetValue(options, out MigrationScope? scope) ? scope : null;

    /// <summary>
    /// The scope to build union routing from: the cached scope while it <see cref="IsActive"/>,
    /// otherwise one discovered from the current resolver, or <see langword="null"/> when the
    /// options no longer carry migration support. Nothing is removed, so this is safe during
    /// resolution.
    /// </summary>
    public static MigrationScope? FindReachable(JsonSerializerOptions options)
    {
        if (FindCached(options) is { } scope && IsActive(scope, options))
        {
            return scope;
        }

        return Discover(options);
    }

    /// <summary>
    /// The registration to honour when <c>AddJsonMigrationSupport()</c> is called again: the cached
    /// scope while it <see cref="IsActive"/>, otherwise one discovered from the current resolver. A
    /// cached scope that is no longer active is dropped so the options count as unregistered.
    /// </summary>
    public static MigrationScope? FindRegistration(JsonSerializerOptions options)
    {
        if (FindCached(options) is { } scope)
        {
            if (IsActive(scope, options))
            {
                return scope;
            }

            Scopes.Remove(options);
        }

        return Discover(options);
    }

    /// <summary>
    /// The scope cached for <paramref name="options"/>, or the one registered on the chain object
    /// they resolve through, re-rooted to them; a copy of registered options arrives that way.
    /// </summary>
    private static MigrationScope? FindCached(JsonSerializerOptions options)
    {
        if (Find(options) is { } scope)
        {
            return scope;
        }

        if (options.TypeInfoResolver is not { } chain)
        {
            return null;
        }

        if (!ScopesByChain.TryGetValue(chain, out MigrationScope? shared))
        {
            shared = FindByLeaf(chain);
        }

        if (shared is null)
        {
            return null;
        }

        var rerooted = new MigrationScope(shared.Resolver, options, []);
        Register(options, rerooted);
        return rerooted;
    }

    /// <summary>
    /// The registration of a chain that shares an application-defined resolver with
    /// <paramref name="chain"/>. A copy that changed its own chain has a chain object of its own,
    /// built from the entries of the chain it was copied from, so the entries are shared; an
    /// application-defined resolver among them that also sits in a registered chain is the same
    /// instance wrapping the same entry there, and that chain's registration is the copy's.
    /// </summary>
    private static MigrationScope? FindByLeaf(IJsonTypeInfoResolver chain)
    {
        foreach (IJsonTypeInfoResolver leaf in ResolverLeaves.Of(chain))
        {
            if (!ResolverProbe.IsApplicationDefined(leaf))
            {
                continue;
            }

            foreach ((IJsonTypeInfoResolver registeredChain, MigrationScope registered) in ScopesByChain)
            {
                if (ResolverLeaves.Contains(registeredChain, leaf))
                {
                    return registered;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the cached <paramref name="scope"/> still serves <paramref name="options"/>: its
    /// resolver is among <see cref="ActiveResolvers"/>, or the chain holds an application-defined
    /// resolver that did not answer the probe. Silence is not removal: such a resolver may forward
    /// only its own application's types and never see the probe's marker while still delegating
    /// every migratable contract to the registration, so the registration is kept. Only a chain
    /// made of STJ's own resolvers, or one whose application-defined resolvers answer with a
    /// different migration resolver, counts as having removed it.
    /// </summary>
    private static bool IsActive(MigrationScope scope, JsonSerializerOptions options)
    {
        bool silentApplicationDefinedLeaf = false;
        foreach (IJsonTypeInfoResolver leaf in ResolverLeaves.Of(options.TypeInfoResolver))
        {
            if (ReferenceEquals(leaf, scope.Resolver))
            {
                return true;
            }

            if (!ResolverProbe.IsApplicationDefined(leaf))
            {
                continue;
            }

            JsonMigrationTypeInfoResolver? behind = ResolverProbe.Through(leaf, options);
            if (ReferenceEquals(behind, scope.Resolver))
            {
                return true;
            }

            silentApplicationDefinedLeaf |= behind is null;
        }

        return silentApplicationDefinedLeaf;
    }

    private static MigrationScope? Discover(JsonSerializerOptions options)
    {
        foreach (JsonMigrationTypeInfoResolver resolver in ActiveResolvers(options))
        {
            var discovered = new MigrationScope(resolver, options, []);
            Register(options, discovered);
            return discovered;
        }

        return null;
    }

    /// <summary>
    /// The migration resolvers <paramref name="options"/> are known to delegate to, in chain order:
    /// those the structural walk reaches, and those an application-defined resolver in the chain
    /// answers the <see cref="ResolverProbe"/> with. The first is the one serving the migratable
    /// types. A resolver that does not answer is not listed and proves nothing either way.
    /// </summary>
    private static IEnumerable<JsonMigrationTypeInfoResolver> ActiveResolvers(JsonSerializerOptions options)
    {
        foreach (IJsonTypeInfoResolver leaf in ResolverLeaves.Of(options.TypeInfoResolver))
        {
            if (leaf is JsonMigrationTypeInfoResolver resolver)
            {
                yield return resolver;
            }
            else if (ResolverProbe.IsApplicationDefined(leaf) && ResolverProbe.Through(leaf, options) is { } behind)
            {
                yield return behind;
            }
        }
    }
}
