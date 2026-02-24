using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.Orleans.StateMigration;

/// <summary>
/// JSON serializer option extensions for state migration scenarios.
/// </summary>
public static class StateMigrationJsonSerializerOptionsExtensions
{
    internal const string DefaultTypePropertyName = "$type";

    /// <summary>
    /// Adds the <see cref="StorageJsonConverterFactory"/> if it is not already registered.
    /// </summary>
    /// <param name="options">The serializer options to configure.</param>
    /// <returns>The same <paramref name="options"/> instance for chaining.</returns>
    /// <remarks>
    /// This is useful with System.Text.Json source generation when a
    /// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> includes state types, but not
    /// closed <see cref="Storage{TStateType}"/> wrapper types.
    /// </remarks>
    /// <example>
    /// <code><![CDATA[
    /// var options = new JsonSerializerOptions(MyStateContext.Default.Options)
    ///     .AddStateMigrationSupport();
    /// ]]></code>
    /// </example>
    public static JsonSerializerOptions AddStateMigrationSupport(this JsonSerializerOptions options)
        => AddStateMigrationSupport(options, DefaultTypePropertyName);

    /// <summary>
    /// Adds state migration serializer support and configures the metadata property name used for state type identity.
    /// </summary>
    /// <param name="options">The serializer options to configure.</param>
    /// <param name="typePropertyName">
    /// The JSON property name containing type identity metadata. Defaults to <c>$type</c>.
    /// </param>
    /// <returns>The same <paramref name="options"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="typePropertyName"/> is null, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Conflicting type property names are configured on the same <see cref="JsonSerializerOptions"/> instance.
    /// </exception>
    public static JsonSerializerOptions AddStateMigrationSupport(this JsonSerializerOptions options, string typePropertyName)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(typePropertyName))
        {
            throw new ArgumentException("Type property name cannot be null or whitespace.", nameof(typePropertyName));
        }

        if (!options.Converters.OfType<StorageJsonConverterFactory>().Any())
        {
            options.Converters.Add(new StorageJsonConverterFactory());
        }

        EnsureTypePropertyNameConfigured(options, typePropertyName);

        EnsureBaselineTypeInfoResolver(options);

        // Source-generated contexts often include state types but not closed Storage<T> wrappers.
        // Add a lightweight resolver so Storage<T> root metadata can be produced without adding each wrapper to context.
        if (!options.TypeInfoResolverChain.OfType<StorageJsonTypeInfoResolver>().Any())
        {
            options.TypeInfoResolverChain.Add(StorageJsonTypeInfoResolver.Instance);
        }

        return options;
    }

    internal static string GetConfiguredTypePropertyName(JsonSerializerOptions options)
        => options.Converters.OfType<StateMigrationJsonConfigurationMarker>().FirstOrDefault()?.TypePropertyName
           ?? DefaultTypePropertyName;

    private static void EnsureTypePropertyNameConfigured(JsonSerializerOptions options, string typePropertyName)
    {
        StateMigrationJsonConfigurationMarker? marker =
            options.Converters.OfType<StateMigrationJsonConfigurationMarker>().FirstOrDefault();

        if (marker is null)
        {
            // Use a converter marker because JsonSerializerOptions has no generic extension-data bag.
            options.Converters.Add(new StateMigrationJsonConfigurationMarker(typePropertyName));
            return;
        }

        if (!string.Equals(marker.TypePropertyName, typePropertyName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Type property name is already configured as '{marker.TypePropertyName}' for this JsonSerializerOptions instance.");
        }
    }

    private static void EnsureBaselineTypeInfoResolver(JsonSerializerOptions options)
    {
        if (options.TypeInfoResolverChain.Any(resolver => resolver is not StorageJsonTypeInfoResolver))
        {
            return;
        }

        // If no baseline resolver exists, add reflection metadata support so non-source-generated options still work.
        options.TypeInfoResolverChain.Insert(0, new DefaultJsonTypeInfoResolver());
    }

    private sealed class StorageJsonTypeInfoResolver : IJsonTypeInfoResolver
    {
        public static StorageJsonTypeInfoResolver Instance { get; } = new();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (!StorageJsonConverterFactory.IsStorageType(type))
            {
                return null;
            }

            return JsonTypeInfo.CreateJsonTypeInfo(type, options);
        }
    }

    private sealed class StateMigrationJsonConfigurationMarker(string typePropertyName) : JsonConverterFactory
    {
        public string TypePropertyName { get; } = typePropertyName;

        public override bool CanConvert(Type typeToConvert) => false;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("Configuration marker does not create converters.");
    }
}
