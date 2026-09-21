using Egil.SystemTextJson.Migration;
using Egil.SystemTextJson.Migration.Migrations;

namespace System.Text.Json;

/// <summary>
/// Adds migration support to <see cref="JsonSerializerOptions"/>.
/// </summary>
public static class JsonMigrationSerializerOptionsExtensions
{
    /// <summary>
    /// Adds migration support using explicit registration and optional assembly scanning.
    /// Migrator types are activated through the provided <paramref name="serviceProvider"/>
    /// and fall back to parameterless construction when not registered.
    /// </summary>
    /// <param name="options">The serializer options to configure.</param>
    /// <param name="serviceProvider">The service provider used to resolve migrator types.</param>
    /// <param name="configure">Optional registration callback.</param>
    /// <returns>The same <paramref name="options"/> instance.</returns>
    public static JsonSerializerOptions AddJsonMigrationSupport(
        this JsonSerializerOptions options,
        IServiceProvider serviceProvider,
        Action<JsonMigrationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return AddJsonMigrationSupport(
            options,
            builder =>
            {
                builder.UseServiceProvider(serviceProvider);
                configure?.Invoke(builder);
            });
    }

    /// <summary>
    /// Adds migration support using explicit registration and optional assembly scanning.
    /// </summary>
    /// <remarks>
    /// Migration support is inserted at the front of <see cref="JsonSerializerOptions.TypeInfoResolverChain"/>.
    /// Resolvers added to the chain afterwards are used for every type that is not <see cref="JsonMigratableAttribute"/>
    /// annotated; replacing the chain or assigning <see cref="JsonSerializerOptions.TypeInfoResolver"/> afterwards
    /// removes migration support. Options that have no resolver keep reflection-based serialization.
    /// </remarks>
    /// <param name="options">The serializer options to configure.</param>
    /// <param name="configure">Optional registration callback.</param>
    /// <returns>The same <paramref name="options"/> instance.</returns>
    public static JsonSerializerOptions AddJsonMigrationSupport(
        this JsonSerializerOptions options,
        Action<JsonMigrationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A second call keeps the first registration, as it did when the converter factory
        // was appended to options.Converters and STJ only ever consulted the first match. Two
        // migration resolvers in the chain would also break the reflection fallback, which
        // only stands in while the migration resolver is the sole entry. Discovery looks through
        // STJ's decorators so a decorated entry counts as registered, and a registration whose
        // resolver was replaced or cleared afterwards does not.
        if (MigrationScope.FindRegistration(options) is not null)
        {
            return options;
        }

        var builder = new JsonMigrationBuilder();
        configure?.Invoke(builder);

        var registry = builder.Build();

        // Migration goes in front of the resolver chain rather than into options.Converters: any
        // entry in that list makes STJ drop source-generated fast-path serialization for every type
        // in the options, while a resolver only costs it for the migratable contracts themselves.
        // The converters already registered keep winning over migration, as they did when the
        // migration converter factory was appended to the list at this point.
        var migrationResolver = new JsonMigrationTypeInfoResolver(registry, options.Converters);
        options.TypeInfoResolverChain.Insert(0, migrationResolver);
        MigrationScope.Register(options, new MigrationScope(migrationResolver, options, []));
#if NET11_0_OR_GREATER
        // Unions whose cases are [JsonMigratable] need a classifier that understands migration
        // discriminators; registering it here means reflection-based users get union support
        // without annotating every union. The classifier finds the registry through the resolver
        // registered above, so the same instance also works when applied via [JsonUnion].
        options.TypeClassifiers.Add(new JsonMigratableUnionTypeClassifier());
#endif
        return options;
    }
}
