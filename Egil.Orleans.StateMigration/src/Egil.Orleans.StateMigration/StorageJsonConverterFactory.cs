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
        Type converterType = typeof(StorageJsonConverter<>).MakeGenericType(stateType);
        string typePropertyName = StateMigrationJsonSerializerOptionsExtensions.GetConfiguredTypePropertyName(options);
        string valuePropertyName = StateMigrationJsonSerializerOptionsExtensions.GetConfiguredValuePropertyName(options);
        StoragePayloadLayout payloadLayout =
            StateMigrationJsonSerializerOptionsExtensions.GetConfiguredPayloadLayout(options);
        return (JsonConverter)Activator.CreateInstance(converterType, typePropertyName, valuePropertyName, payloadLayout)!;
    }

    private sealed class StorageJsonConverter<TStateType> : JsonConverter<Storage<TStateType>>
    {
        private const string LegacyValuePropertyName = "value";
        private enum EnvelopeValuePropertyMatch
        {
            None,
            Configured,
            Legacy,
        }

        private static readonly string TargetTypeIdentity = StateTypeIdentity.GetIdentity(typeof(TStateType));
        private readonly string _typePropertyName;
        private readonly string _valuePropertyName;
        private readonly StoragePayloadLayout _payloadLayout;

        public StorageJsonConverter(
            string typePropertyName,
            string valuePropertyName,
            StoragePayloadLayout payloadLayout)
        {
            _typePropertyName = typePropertyName;
            _valuePropertyName = valuePropertyName;
            _payloadLayout = payloadLayout;
        }

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
            if (string.Equals(firstPropertyName, _typePropertyName, StringComparison.Ordinal))
            {
                if (!probe.Read())
                {
                    throw new JsonException($"Storage payload is missing a '{_typePropertyName}' value.");
                }

                string? sourceIdentity = ParseTypeIdentity(ref probe);

                if (string.IsNullOrWhiteSpace(sourceIdentity))
                {
                    throw new JsonException($"Storage payload '{_typePropertyName}' cannot be null or empty.");
                }

                EnvelopeValuePropertyMatch envelopeValuePropertyMatch = ProbeEnvelopedPayload(ref probe);
                bool isEnvelopedPayload = envelopeValuePropertyMatch is not EnvelopeValuePropertyMatch.None;
                StoragePayloadLayout incomingLayout = isEnvelopedPayload
                    ? StoragePayloadLayout.Enveloped
                    : StoragePayloadLayout.Flattened;

                if (string.Equals(sourceIdentity, TargetTypeIdentity, StringComparison.Ordinal))
                {
                    TStateType? state = isEnvelopedPayload
                        ? DeserializeEnvelopedState(ref reader, sourceIdentity, options)
                        : DeserializeCurrentFlattenedPayload(ref reader, options);
                    if (state is null)
                    {
                        throw new JsonException("Storage payload produced a null state for the current type.");
                    }

                    return new Storage<TStateType>
                    {
                        Value = InvokeOnDeserializedCallback(state),
                        // Rewrite when incoming payload shape or envelope field name differs from configured output.
                        MigratedDuringDeserialization = incomingLayout != _payloadLayout
                                                       || envelopeValuePropertyMatch is EnvelopeValuePropertyMatch.Legacy,
                    };
                }

                if (!StateTypeIdentity.TryResolve(sourceIdentity, out Type? sourceType))
                {
                    throw new JsonException($"Storage payload type '{sourceIdentity}' is unknown.");
                }

                object? source = isEnvelopedPayload
                    ? DeserializeEnvelopedState(ref reader, sourceIdentity, sourceType, options)
                    : JsonSerializer.Deserialize(ref reader, sourceType, options);
                if (source is null)
                {
                    throw new JsonException($"Storage payload could not deserialize source type '{sourceIdentity}'.");
                }

                if (!StateMigrationInvoker.TryMigrate(source, sourceType, typeof(TStateType), out object? migratedState))
                {
                    throw new JsonException(
                        $"No direct migration exists from '{sourceIdentity}' to '{TargetTypeIdentity}'.");
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
            if (_payloadLayout == StoragePayloadLayout.Enveloped)
            {
                writer.WriteStartObject();
                writer.WriteString(_typePropertyName, TargetTypeIdentity);
                writer.WritePropertyName(_valuePropertyName);
                // Envelope payload avoids JsonElement materialization and supports any JSON shape for TStateType.
                JsonSerializer.Serialize(writer, value.Value, options);
                writer.WriteEndObject();
                return;
            }

            WriteFlattenedPayload(writer, value, options);
        }

        private string? ParseTypeIdentity(ref Utf8JsonReader reader)
            => reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Null => null,
                _ => throw new JsonException($"Storage payload '{_typePropertyName}' must be a string."),
            };

        private EnvelopeValuePropertyMatch ProbeEnvelopedPayload(ref Utf8JsonReader probe)
        {
            if (!probe.Read() || probe.TokenType != JsonTokenType.PropertyName)
            {
                return EnvelopeValuePropertyMatch.None;
            }

            string? propertyName = probe.GetString();
            EnvelopeValuePropertyMatch match = GetEnvelopeValuePropertyMatch(propertyName);
            if (match is EnvelopeValuePropertyMatch.None)
            {
                return EnvelopeValuePropertyMatch.None;
            }

            if (!probe.Read())
            {
                throw new JsonException($"Storage envelope payload is missing a '{_valuePropertyName}' value.");
            }

            probe.Skip();
            return probe.Read() && probe.TokenType == JsonTokenType.EndObject
                ? match
                : EnvelopeValuePropertyMatch.None;
        }

        private TStateType? DeserializeCurrentFlattenedPayload(ref Utf8JsonReader reader, JsonSerializerOptions options)
            => JsonSerializer.Deserialize<TStateType>(ref reader, options);

        private TStateType? DeserializeEnvelopedState(
            ref Utf8JsonReader reader,
            string expectedSourceIdentity,
            JsonSerializerOptions options)
            => (TStateType?)DeserializeEnvelopedState(ref reader, expectedSourceIdentity, typeof(TStateType), options);

        private object? DeserializeEnvelopedState(
            ref Utf8JsonReader reader,
            string expectedSourceIdentity,
            Type stateType,
            JsonSerializerOptions options)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Storage payload is malformed.");
            }

            if (!string.Equals(reader.GetString(), _typePropertyName, StringComparison.Ordinal))
            {
                throw new JsonException($"Storage payload must start with '{_typePropertyName}'.");
            }

            if (!reader.Read())
            {
                throw new JsonException($"Storage payload is missing a '{_typePropertyName}' value.");
            }

            string? sourceIdentity = ParseTypeIdentity(ref reader);
            if (!string.Equals(sourceIdentity, expectedSourceIdentity, StringComparison.Ordinal))
            {
                throw new JsonException("Storage payload type metadata changed while parsing.");
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Storage payload is missing a '{_valuePropertyName}' property.");
            }

            EnvelopeValuePropertyMatch match = GetEnvelopeValuePropertyMatch(reader.GetString());
            if (match is EnvelopeValuePropertyMatch.None)
            {
                throw new JsonException(
                    $"Storage payload must use '{_valuePropertyName}' as the state property name.");
            }

            if (!reader.Read())
            {
                throw new JsonException($"Storage payload is missing a '{_valuePropertyName}' value.");
            }

            object? state = JsonSerializer.Deserialize(ref reader, stateType, options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
            {
                throw new JsonException("Storage envelope payload contains unexpected properties.");
            }

            return state;
        }

        private EnvelopeValuePropertyMatch GetEnvelopeValuePropertyMatch(string? propertyName)
        {
            if (string.Equals(propertyName, _valuePropertyName, StringComparison.Ordinal))
            {
                return EnvelopeValuePropertyMatch.Configured;
            }

            if (string.Equals(propertyName, LegacyValuePropertyName, StringComparison.Ordinal))
            {
                return EnvelopeValuePropertyMatch.Legacy;
            }

            return EnvelopeValuePropertyMatch.None;
        }

        private void WriteFlattenedPayload(
            Utf8JsonWriter writer,
            Storage<TStateType> value,
            JsonSerializerOptions options)
        {
            JsonElement stateElement = JsonSerializer.SerializeToElement(value.Value, options);
            if (stateElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException(
                    $"Flattened storage payload requires '{typeof(TStateType)}' to serialize as a JSON object. " +
                    $"Use '{nameof(StoragePayloadLayout)}.{nameof(StoragePayloadLayout.Enveloped)}' for non-object states.");
            }

            if (stateElement.TryGetProperty(_typePropertyName, out _))
            {
                throw new JsonException($"State payload cannot define a '{_typePropertyName}' property.");
            }

            writer.WriteStartObject();
            writer.WriteString(_typePropertyName, TargetTypeIdentity);
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
