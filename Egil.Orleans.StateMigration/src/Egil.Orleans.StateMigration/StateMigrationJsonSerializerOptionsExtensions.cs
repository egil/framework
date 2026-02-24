using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Egil.Orleans.StateMigration;

/// <summary>
/// JSON serializer option extensions for state migration scenarios.
/// </summary>
public static class StateMigrationJsonSerializerOptionsExtensions
{
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
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Converters.OfType<StorageJsonConverterFactory>().Any())
        {
            options.Converters.Add(new StorageJsonConverterFactory());
        }

        // Source-generated contexts often include state types but not closed Storage<T> wrappers.
        // Add a lightweight resolver so Storage<T> root metadata can be produced without adding each wrapper to context.
        if (!options.TypeInfoResolverChain.OfType<StorageJsonTypeInfoResolver>().Any())
        {
            options.TypeInfoResolverChain.Add(StorageJsonTypeInfoResolver.Instance);
        }

        return options;
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
}
