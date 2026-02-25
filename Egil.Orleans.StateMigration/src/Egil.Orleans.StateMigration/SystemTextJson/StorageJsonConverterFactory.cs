using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.StateMigration.SystemTextJson;

/// <summary>
/// Creates JSON converters for <see cref="Storage{TStateType}"/>.
/// </summary>
/// <remarks>
/// The converter enforces the storage payload contract described in this library:
/// versioned state with type identity and migration-aware deserialization behavior.
/// </remarks>
/// <example>
/// <code><![CDATA[
/// Storage<CartState> storage = JsonSerializer.Deserialize<Storage<CartState>>(json)!;
/// ]]></code>
/// </example>
public sealed class StorageJsonConverterFactory : JsonConverterFactory
{
    private readonly IServiceProvider? _serviceProvider;

    public StorageJsonConverterFactory()
    {
    }

    internal StorageJsonConverterFactory(IServiceProvider? serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    internal IServiceProvider? ServiceProvider => _serviceProvider;

    /// <summary>
    /// Determines whether this factory can produce a converter for the provided type.
    /// </summary>
    /// <param name="typeToConvert">The requested type.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="typeToConvert"/> is <see cref="Storage{TStateType}"/>;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public override bool CanConvert(Type typeToConvert)
        => IsStorageType(typeToConvert);

    internal static bool IsStorageType(Type typeToConvert)
        => typeToConvert.IsGenericType
           && typeToConvert.GetGenericTypeDefinition() == typeof(Storage<>);

    /// <summary>
    /// Creates a converter for a closed <see cref="Storage{TStateType}"/> type.
    /// </summary>
    /// <param name="typeToConvert">The concrete storage type to convert.</param>
    /// <param name="options">JSON serializer options.</param>
    /// <returns>A converter instance for the requested storage type.</returns>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type stateType = typeToConvert.GetGenericArguments()[0];
        string typePropertyName = StateMigrationJsonSerializerOptionsExtensions.GetConfiguredTypePropertyName(options);
        string valuePropertyName = StateMigrationJsonSerializerOptionsExtensions.GetConfiguredValuePropertyName(options);
        StoragePayloadLayout payloadLayout =
            StateMigrationJsonSerializerOptionsExtensions.GetConfiguredPayloadLayout(options);
        Type converterType = payloadLayout switch
        {
            StoragePayloadLayout.Enveloped => typeof(EnvelopedStorageJsonConverter<>).MakeGenericType(stateType),
            StoragePayloadLayout.Flattened => typeof(FlattenedStorageJsonConverter<>).MakeGenericType(stateType),
            _ => throw new JsonException($"Unsupported storage payload layout '{payloadLayout}'."),
        };

        return (JsonConverter)Activator.CreateInstance(
            converterType,
            typePropertyName,
            valuePropertyName,
            options,
            _serviceProvider)!;
    }
}
