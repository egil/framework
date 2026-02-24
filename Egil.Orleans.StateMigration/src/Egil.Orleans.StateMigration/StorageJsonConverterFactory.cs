using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.StateMigration;

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
    /// <summary>
    /// Determines whether this factory can produce a converter for the provided type.
    /// </summary>
    /// <param name="typeToConvert">The requested type.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="typeToConvert"/> is <see cref="Storage{TStateType}"/>;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public override bool CanConvert(Type typeToConvert)
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
        Type converterType = typeof(StorageJsonConverter<>).MakeGenericType(stateType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class StorageJsonConverter<TStateType> : JsonConverter<Storage<TStateType>>
    {
        public override Storage<TStateType>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotImplementedException("Storage deserialization is implemented in a later phase.");

        public override void Write(Utf8JsonWriter writer, Storage<TStateType> value, JsonSerializerOptions options)
            => throw new NotImplementedException("Storage serialization is implemented in a later phase.");
    }
}
