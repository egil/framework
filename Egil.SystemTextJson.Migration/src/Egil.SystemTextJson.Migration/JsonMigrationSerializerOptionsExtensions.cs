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
    /// <param name="options">The serializer options to configure.</param>
    /// <param name="configure">Optional registration callback.</param>
    /// <returns>The same <paramref name="options"/> instance.</returns>
    public static JsonSerializerOptions AddJsonMigrationSupport(
        this JsonSerializerOptions options,
        Action<JsonMigrationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new JsonMigrationBuilder();
        configure?.Invoke(builder);

        var registry = builder.Build();
        options.Converters.Add(new JsonMigratableConverterFactory(registry));
#if NET11_0_OR_GREATER
        // Unions whose cases are [JsonMigratable] need a classifier that understands migration
        // discriminators; registering it here means reflection-based users get union support
        // without annotating every union. The factory finds the registry through the converter
        // registered above, so the same instance also works when applied via [JsonUnion].
        options.TypeClassifiers.Add(new JsonMigratableUnionTypeClassifier());
#endif
        return options;
    }
}
