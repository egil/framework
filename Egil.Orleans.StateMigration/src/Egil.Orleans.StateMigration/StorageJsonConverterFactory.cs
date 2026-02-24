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
        private const string TypePropertyName = "$type";

        public override Storage<TStateType>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                // Legacy payloads for collection/primitive states are often non-object JSON (for example arrays, numbers, strings).
                return DeserializeLegacyPayload(ref reader, options);
            }

            var probe = reader;
            if (!probe.Read() || probe.TokenType == JsonTokenType.EndObject)
            {
                return DeserializeLegacyPayload(ref reader, options);
            }

            if (probe.TokenType != JsonTokenType.PropertyName)
            {
                return DeserializeLegacyPayload(ref reader, options);
            }

            string? firstPropertyName = probe.GetString();
            if (string.Equals(firstPropertyName, TypePropertyName, StringComparison.Ordinal))
            {
                if (!probe.Read())
                {
                    throw new JsonException("Storage payload is missing a $type value.");
                }

                string? sourceIdentity = probe.TokenType switch
                {
                    JsonTokenType.String => probe.GetString(),
                    JsonTokenType.Null => null,
                    _ => throw new JsonException("Storage payload $type must be a string."),
                };

                if (string.IsNullOrWhiteSpace(sourceIdentity))
                {
                    throw new JsonException("Storage payload $type cannot be null or empty.");
                }

                string targetIdentity = StateTypeIdentity.GetIdentity(typeof(TStateType));
                if (string.Equals(sourceIdentity, targetIdentity, StringComparison.Ordinal))
                {
                    TStateType? state = JsonSerializer.Deserialize<TStateType>(ref reader, options);
                    if (state is null)
                    {
                        throw new JsonException("Storage payload produced a null state for the current type.");
                    }

                    return new Storage<TStateType>
                    {
                        Value = InvokeOnDeserializedCallback(state),
                        MigratedDuringDeserialization = false,
                    };
                }

                if (!StateTypeIdentity.TryResolve(sourceIdentity, out Type? sourceType))
                {
                    throw new JsonException($"Storage payload type '{sourceIdentity}' is unknown.");
                }

                object? source = JsonSerializer.Deserialize(ref reader, sourceType, options);
                if (source is null)
                {
                    throw new JsonException($"Storage payload could not deserialize source type '{sourceIdentity}'.");
                }

                if (!StateMigrationInvoker.TryMigrate(source, sourceType, typeof(TStateType), out object? migratedState))
                {
                    throw new JsonException(
                        $"No direct migration exists from '{sourceIdentity}' to '{targetIdentity}'.");
                }

                return new Storage<TStateType>
                {
                    Value = InvokeOnDeserializedCallback((TStateType)migratedState!),
                    MigratedDuringDeserialization = true,
                };
            }

            return DeserializeLegacyPayload(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, Storage<TStateType> value, JsonSerializerOptions options)
        {
            JsonElement stateElement = JsonSerializer.SerializeToElement(value.Value, options);
            if (stateElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Storage state must serialize to a JSON object.");
            }

            // Reserve $type as metadata contract to keep read-path probing deterministic.
            if (stateElement.TryGetProperty(TypePropertyName, out _))
            {
                throw new JsonException("State payload cannot define a '$type' property.");
            }

            writer.WriteStartObject();
            writer.WriteString(TypePropertyName, StateTypeIdentity.GetIdentity(typeof(TStateType)));
            foreach (JsonProperty property in stateElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        private static TStateType InvokeOnDeserializedCallback(TStateType state)
        {
            if (state is global::Orleans.Serialization.IOnDeserialized callback)
            {
                // Use Orleans callback hook after materialization so states can perform post-deserialization fixups.
                callback.OnDeserialized(default!);
            }

            return state;
        }

        private static Storage<TStateType> DeserializeLegacyPayload(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            // Missing leading $type means legacy/unversioned payload. Mark migrated so callers can persist versioned format.
            TStateType? legacyState = JsonSerializer.Deserialize<TStateType>(ref reader, options);
            if (legacyState is null)
            {
                throw new JsonException("Legacy storage payload produced a null state.");
            }

            return new Storage<TStateType>
            {
                Value = InvokeOnDeserializedCallback(legacyState),
                MigratedDuringDeserialization = true,
            };
        }
    }
}
