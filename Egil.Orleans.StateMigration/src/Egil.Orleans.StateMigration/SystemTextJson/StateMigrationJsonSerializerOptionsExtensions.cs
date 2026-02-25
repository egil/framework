using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Egil.Orleans.StateMigration;
using Egil.Orleans.StateMigration.SystemTextJson;

namespace System.Text.Json;

/// <summary>
/// JSON serializer option extensions for state migration scenarios.
/// </summary>
public static class StateMigrationJsonSerializerOptionsExtensions
{
    internal const string DefaultTypePropertyName = "$type";
    internal const string DefaultValuePropertyName = "$value";

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
        => AddStateMigrationSupport(
            options,
            DefaultTypePropertyName,
            DefaultValuePropertyName,
            StoragePayloadLayout.Enveloped);

    /// <summary>
    /// Adds state migration serializer support and configures payload layout.
    /// </summary>
    /// <param name="options">The serializer options to configure.</param>
    /// <param name="payloadLayout">
    /// The JSON payload layout for <see cref="Storage{TStateType}"/>. Use
    /// <see cref="StoragePayloadLayout.Enveloped"/> for the default low-overhead hot path.
    /// </param>
    /// <returns>The same <paramref name="options"/> instance for chaining.</returns>
    public static JsonSerializerOptions AddStateMigrationSupport(this JsonSerializerOptions options, StoragePayloadLayout payloadLayout)
        => AddStateMigrationSupport(options, DefaultTypePropertyName, DefaultValuePropertyName, payloadLayout);

    /// <summary>
    /// Adds state migration serializer support and configures the metadata property name used for state type identity.
    /// </summary>
    /// <param name="options">The serializer options to configure.</param>
    /// <param name="typePropertyName">
    /// The JSON property name containing type identity metadata. Defaults to <c>$type</c>.
    /// Enveloped payload state remains under the default <c>$value</c> property unless explicitly configured.
    /// </param>
    /// <returns>The same <paramref name="options"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="typePropertyName"/> is null, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Conflicting metadata configuration is detected on the same <see cref="JsonSerializerOptions"/> instance.
    /// </exception>
    public static JsonSerializerOptions AddStateMigrationSupport(this JsonSerializerOptions options, string typePropertyName)
        => AddStateMigrationSupport(
            options,
            typePropertyName,
            DefaultValuePropertyName,
            StoragePayloadLayout.Enveloped);

    /// <summary>
    /// Adds state migration serializer support and configures both metadata property names.
    /// </summary>
    /// <param name="options">The serializer options to configure.</param>
    /// <param name="typePropertyName">The JSON property name containing type identity metadata.</param>
    /// <param name="valuePropertyName">The JSON property name containing wrapped state in enveloped layout.</param>
    /// <returns>The same <paramref name="options"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="typePropertyName"/> or <paramref name="valuePropertyName"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Conflicting property names are configured on the same <see cref="JsonSerializerOptions"/> instance.
    /// </exception>
    public static JsonSerializerOptions AddStateMigrationSupport(
        this JsonSerializerOptions options,
        string typePropertyName,
        string valuePropertyName)
        => AddStateMigrationSupport(options, typePropertyName, valuePropertyName, StoragePayloadLayout.Enveloped);

    /// <summary>
    /// Adds state migration serializer support and configures metadata property name and payload layout.
    /// </summary>
    /// <param name="options">The serializer options to configure.</param>
    /// <param name="typePropertyName">The JSON property name containing type identity metadata.</param>
    /// <param name="payloadLayout">
    /// The JSON payload layout for <see cref="Storage{TStateType}"/>. Use
    /// <see cref="StoragePayloadLayout.Flattened"/> only when legacy shape compatibility is required.
    /// </param>
    /// <returns>The same <paramref name="options"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="typePropertyName"/> is null, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Conflicting metadata configuration or payload layout is detected on the same
    /// <see cref="JsonSerializerOptions"/> instance.
    /// </exception>
    public static JsonSerializerOptions AddStateMigrationSupport(
        this JsonSerializerOptions options,
        string typePropertyName,
        StoragePayloadLayout payloadLayout)
        => AddStateMigrationSupport(options, typePropertyName, DefaultValuePropertyName, payloadLayout);

    /// <summary>
    /// Adds state migration serializer support and configures metadata property names and payload layout.
    /// </summary>
    /// <param name="options">The serializer options to configure.</param>
    /// <param name="typePropertyName">The JSON property name containing type identity metadata.</param>
    /// <param name="valuePropertyName">The JSON property name containing wrapped state in enveloped layout.</param>
    /// <param name="payloadLayout">
    /// The JSON payload layout for <see cref="Storage{TStateType}"/>. Use
    /// <see cref="StoragePayloadLayout.Flattened"/> only when legacy shape compatibility is required.
    /// </param>
    /// <returns>The same <paramref name="options"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="typePropertyName"/> or <paramref name="valuePropertyName"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Conflicting type/value property names or payload layouts are configured on the same
    /// <see cref="JsonSerializerOptions"/> instance.
    /// </exception>
    public static JsonSerializerOptions AddStateMigrationSupport(
        this JsonSerializerOptions options,
        string typePropertyName,
        string valuePropertyName,
        StoragePayloadLayout payloadLayout)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(typePropertyName))
        {
            throw new ArgumentException("Type property name cannot be null or whitespace.", nameof(typePropertyName));
        }

        if (string.IsNullOrWhiteSpace(valuePropertyName))
        {
            throw new ArgumentException("Value property name cannot be null or whitespace.", nameof(valuePropertyName));
        }

        if (!options.Converters.OfType<StorageJsonConverterFactory>().Any())
        {
            options.Converters.Add(new StorageJsonConverterFactory());
        }

        EnsureConfigurationConfigured(options, typePropertyName, valuePropertyName, payloadLayout);

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

    internal static string GetConfiguredValuePropertyName(JsonSerializerOptions options)
        => options.Converters.OfType<StateMigrationJsonConfigurationMarker>().FirstOrDefault()?.ValuePropertyName
           ?? DefaultValuePropertyName;

    internal static StoragePayloadLayout GetConfiguredPayloadLayout(JsonSerializerOptions options)
        => options.Converters.OfType<StateMigrationJsonConfigurationMarker>().FirstOrDefault()?.PayloadLayout
           ?? StoragePayloadLayout.Enveloped;

    private static void EnsureConfigurationConfigured(
        JsonSerializerOptions options,
        string typePropertyName,
        string valuePropertyName,
        StoragePayloadLayout payloadLayout)
    {
        StateMigrationJsonConfigurationMarker? marker =
            options.Converters.OfType<StateMigrationJsonConfigurationMarker>().FirstOrDefault();

        if (marker is null)
        {
            // Use a converter marker because JsonSerializerOptions has no generic extension-data bag.
            options.Converters.Add(
                new StateMigrationJsonConfigurationMarker(typePropertyName, valuePropertyName, payloadLayout));
            return;
        }

        if (!string.Equals(marker.TypePropertyName, typePropertyName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Type property name is already configured as '{marker.TypePropertyName}' for this JsonSerializerOptions instance.");
        }

        if (!string.Equals(marker.ValuePropertyName, valuePropertyName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Value property name is already configured as '{marker.ValuePropertyName}' for this JsonSerializerOptions instance.");
        }

        if (marker.PayloadLayout != payloadLayout)
        {
            throw new InvalidOperationException(
                $"Storage payload layout is already configured as '{marker.PayloadLayout}' for this JsonSerializerOptions instance.");
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

    private sealed class StateMigrationJsonConfigurationMarker(
        string typePropertyName,
        string valuePropertyName,
        StoragePayloadLayout payloadLayout) : JsonConverterFactory
    {
        public string TypePropertyName { get; } = typePropertyName;
        public string ValuePropertyName { get; } = valuePropertyName;
        public StoragePayloadLayout PayloadLayout { get; } = payloadLayout;

        public override bool CanConvert(Type typeToConvert) => false;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("Configuration marker does not create converters.");
    }
}
