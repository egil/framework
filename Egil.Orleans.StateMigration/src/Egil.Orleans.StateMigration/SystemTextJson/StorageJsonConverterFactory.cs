using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

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

        return (JsonConverter)Activator.CreateInstance(converterType, typePropertyName, valuePropertyName)!;
    }

    private sealed class EnvelopedStorageJsonConverter<TStateType> : JsonConverter<Storage<TStateType>>
    {
        private enum EnvelopeValuePropertyMatch
        {
            None,
            Configured,
        }

        private static readonly string TargetTypeIdentity = StateTypeIdentity.GetIdentity(typeof(TStateType));
        private static readonly byte[] TargetTypeIdentityUtf8 = Encoding.UTF8.GetBytes(TargetTypeIdentity);
        private readonly string _typePropertyName;
        private readonly string _valuePropertyName;
        private readonly byte[] _typePropertyNameUtf8;
        private readonly byte[] _valuePropertyNameUtf8;

        public EnvelopedStorageJsonConverter(string typePropertyName, string valuePropertyName)
        {
            _typePropertyName = typePropertyName;
            _valuePropertyName = valuePropertyName;
            _typePropertyNameUtf8 = Encoding.UTF8.GetBytes(typePropertyName);
            _valuePropertyNameUtf8 = Encoding.UTF8.GetBytes(valuePropertyName);
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

            if (probe.ValueTextEquals(_typePropertyNameUtf8))
            {
                if (!probe.Read())
                {
                    throw new JsonException($"Storage payload is missing a '{_typePropertyName}' value.");
                }

                string? sourceIdentity = ParseTypeIdentity(ref probe, out bool sourceIsTargetType);
                if (!sourceIsTargetType && string.IsNullOrWhiteSpace(sourceIdentity))
                {
                    throw new JsonException($"Storage payload '{_typePropertyName}' cannot be null or empty.");
                }

                if (sourceIsTargetType)
                {
                    // Hot path optimization: avoid full probe + skip when source type already matches target.
                    // We only peek the next property name, then parse once with a safe fallback to flattened.
                    var envelopeProbe = probe;
                    bool hasSecondPropertyName =
                        envelopeProbe.Read()
                        && envelopeProbe.TokenType == JsonTokenType.PropertyName;
                    bool mayBeEnvelopedPayload =
                        hasSecondPropertyName
                        && GetEnvelopeValuePropertyMatch(ref envelopeProbe) is not EnvelopeValuePropertyMatch.None;

                    if (!mayBeEnvelopedPayload
                        && hasSecondPropertyName
                        && envelopeProbe.ValueTextEquals("value"u8))
                    {
                        throw new JsonException(
                            $"Storage payload must use '{_valuePropertyName}' as the state property name.");
                    }

                    if (mayBeEnvelopedPayload
                        && TryDeserializeEnvelopedCurrentState(
                            ref reader,
                            options,
                            out TStateType? envelopedState))
                    {
                        if (envelopedState is null)
                        {
                            throw new JsonException("Storage payload produced a null state for the current type.");
                        }

                        return new Storage<TStateType>
                        {
                            Value = InvokeOnDeserializedCallback(envelopedState),
                            // Rewrite when incoming payload shape differs from configured output.
                            MigratedDuringDeserialization = false,
                        };
                    }

                    // Fallback: flattened payload (or edge-case object where first flattened property is named like envelope state field).
                    TStateType? state = DeserializeCurrentFlattenedPayload(ref reader, options);
                    if (state is null)
                    {
                        throw new JsonException("Storage payload produced a null state for the current type.");
                    }

                    return new Storage<TStateType>
                    {
                        Value = InvokeOnDeserializedCallback(state),
                        MigratedDuringDeserialization = true,
                    };
                }

                if (sourceIdentity is null)
                {
                    throw new JsonException($"Storage payload '{_typePropertyName}' cannot be null or empty.");
                }

                if (!StateTypeIdentity.TryResolve(sourceIdentity, out Type? sourceType))
                {
                    throw new JsonException($"Storage payload type '{sourceIdentity}' is unknown.");
                }

                EnvelopeValuePropertyMatch envelopeValuePropertyMatch = ProbeEnvelopedPayload(ref probe);
                bool isEnvelopedPayload = envelopeValuePropertyMatch is not EnvelopeValuePropertyMatch.None;
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
            writer.WriteStartObject();
            writer.WriteString(_typePropertyName, TargetTypeIdentity);
            writer.WritePropertyName(_valuePropertyName);
            // Envelope payload avoids JsonElement materialization and supports any JSON shape for TStateType.
            JsonSerializer.Serialize(writer, value.Value, options);
            writer.WriteEndObject();
        }

        private string? ParseTypeIdentity(ref Utf8JsonReader reader, out bool isTargetType)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    if (reader.ValueTextEquals(TargetTypeIdentityUtf8))
                    {
                        isTargetType = true;
                        return TargetTypeIdentity;
                    }

                    isTargetType = false;
                    return reader.GetString();
                case JsonTokenType.Null:
                    isTargetType = false;
                    return null;
                default:
                    throw new JsonException($"Storage payload '{_typePropertyName}' must be a string.");
            }
        }

        private EnvelopeValuePropertyMatch ProbeEnvelopedPayload(ref Utf8JsonReader probe)
        {
            if (!probe.Read() || probe.TokenType != JsonTokenType.PropertyName)
            {
                return EnvelopeValuePropertyMatch.None;
            }

            EnvelopeValuePropertyMatch match = GetEnvelopeValuePropertyMatch(ref probe);
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

        private bool TryDeserializeEnvelopedCurrentState(
            ref Utf8JsonReader reader,
            JsonSerializerOptions options,
            out TStateType? state)
        {
            var originalReader = reader;
            state = default;

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals(_typePropertyNameUtf8))
            {
                reader = originalReader;
                return false;
            }

            if (!reader.Read())
            {
                throw new JsonException($"Storage payload is missing a '{_typePropertyName}' value.");
            }

            if (reader.TokenType != JsonTokenType.String || !reader.ValueTextEquals(TargetTypeIdentityUtf8))
            {
                reader = originalReader;
                return false;
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName)
            {
                reader = originalReader;
                return false;
            }

            if (GetEnvelopeValuePropertyMatch(ref reader) is EnvelopeValuePropertyMatch.None)
            {
                reader = originalReader;
                return false;
            }

            if (!reader.Read())
            {
                throw new JsonException($"Storage payload is missing a '{_valuePropertyName}' value.");
            }

            state = JsonSerializer.Deserialize<TStateType>(ref reader, options);
            if (!reader.Read())
            {
                throw new JsonException("Storage payload is malformed.");
            }

            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return true;
            }

            // Flattened edge case: the first flattened property name matched the envelope state field name.
            // Reset and let the flattened path consume the payload.
            reader = originalReader;
            state = default;
            return false;
        }

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

            string? sourceIdentity = ParseTypeIdentity(ref reader, out _);
            if (!string.Equals(sourceIdentity, expectedSourceIdentity, StringComparison.Ordinal))
            {
                throw new JsonException("Storage payload type metadata changed while parsing.");
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Storage payload is missing a '{_valuePropertyName}' property.");
            }

            EnvelopeValuePropertyMatch match = GetEnvelopeValuePropertyMatch(ref reader);
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

        private EnvelopeValuePropertyMatch GetEnvelopeValuePropertyMatch(ref Utf8JsonReader reader)
        {
            if (reader.ValueTextEquals(_valuePropertyNameUtf8))
            {
                return EnvelopeValuePropertyMatch.Configured;
            }

            return EnvelopeValuePropertyMatch.None;
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

    private sealed class FlattenedStorageJsonConverter<TStateType> : JsonConverter<Storage<TStateType>>
    {
        private enum EnvelopeValuePropertyMatch
        {
            None,
            Configured,
        }

        private static readonly string TargetTypeIdentity = StateTypeIdentity.GetIdentity(typeof(TStateType));
        private static readonly byte[] TargetTypeIdentityUtf8 = Encoding.UTF8.GetBytes(TargetTypeIdentity);
        private readonly string _typePropertyName;
        private readonly string _valuePropertyName;
        private readonly byte[] _typePropertyNameUtf8;
        private readonly byte[] _valuePropertyNameUtf8;

        public FlattenedStorageJsonConverter(string typePropertyName, string valuePropertyName)
        {
            _typePropertyName = typePropertyName;
            _valuePropertyName = valuePropertyName;
            _typePropertyNameUtf8 = Encoding.UTF8.GetBytes(typePropertyName);
            _valuePropertyNameUtf8 = Encoding.UTF8.GetBytes(valuePropertyName);
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

            if (probe.ValueTextEquals(_typePropertyNameUtf8))
            {
                if (!probe.Read())
                {
                    throw new JsonException($"Storage payload is missing a '{_typePropertyName}' value.");
                }

                string? sourceIdentity = ParseTypeIdentity(ref probe, out bool sourceIsTargetType);
                if (!sourceIsTargetType && string.IsNullOrWhiteSpace(sourceIdentity))
                {
                    throw new JsonException($"Storage payload '{_typePropertyName}' cannot be null or empty.");
                }

                if (sourceIsTargetType)
                {
                    // Hot path optimization: avoid full probe + skip when source type already matches target.
                    // We only peek the next property name, then parse once with a safe fallback to flattened.
                    var envelopeProbe = probe;
                    bool hasSecondPropertyName =
                        envelopeProbe.Read()
                        && envelopeProbe.TokenType == JsonTokenType.PropertyName;
                    bool mayBeEnvelopedPayload =
                        hasSecondPropertyName
                        && GetEnvelopeValuePropertyMatch(ref envelopeProbe) is not EnvelopeValuePropertyMatch.None;

                    if (mayBeEnvelopedPayload
                        && TryDeserializeEnvelopedCurrentState(
                            ref reader,
                            options,
                            out TStateType? envelopedState))
                    {
                        if (envelopedState is null)
                        {
                            throw new JsonException("Storage payload produced a null state for the current type.");
                        }

                        return new Storage<TStateType>
                        {
                            Value = InvokeOnDeserializedCallback(envelopedState),
                            // Rewrite when incoming payload shape differs from configured output.
                            MigratedDuringDeserialization = true,
                        };
                    }

                    // Fallback: flattened payload (or edge-case object where first flattened property is named like envelope state field).
                    TStateType? state = DeserializeCurrentFlattenedPayload(ref reader, options);
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

                if (sourceIdentity is null)
                {
                    throw new JsonException($"Storage payload '{_typePropertyName}' cannot be null or empty.");
                }

                if (!StateTypeIdentity.TryResolve(sourceIdentity, out Type? sourceType))
                {
                    throw new JsonException($"Storage payload type '{sourceIdentity}' is unknown.");
                }

                EnvelopeValuePropertyMatch envelopeValuePropertyMatch = ProbeEnvelopedPayload(ref probe);
                bool isEnvelopedPayload = envelopeValuePropertyMatch is not EnvelopeValuePropertyMatch.None;
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
            WriteFlattenedPayload(writer, value, options);
        }

        private string? ParseTypeIdentity(ref Utf8JsonReader reader, out bool isTargetType)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    if (reader.ValueTextEquals(TargetTypeIdentityUtf8))
                    {
                        isTargetType = true;
                        return TargetTypeIdentity;
                    }

                    isTargetType = false;
                    return reader.GetString();
                case JsonTokenType.Null:
                    isTargetType = false;
                    return null;
                default:
                    throw new JsonException($"Storage payload '{_typePropertyName}' must be a string.");
            }
        }

        private EnvelopeValuePropertyMatch ProbeEnvelopedPayload(ref Utf8JsonReader probe)
        {
            if (!probe.Read() || probe.TokenType != JsonTokenType.PropertyName)
            {
                return EnvelopeValuePropertyMatch.None;
            }

            EnvelopeValuePropertyMatch match = GetEnvelopeValuePropertyMatch(ref probe);
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

        private bool TryDeserializeEnvelopedCurrentState(
            ref Utf8JsonReader reader,
            JsonSerializerOptions options,
            out TStateType? state)
        {
            var originalReader = reader;
            state = default;

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals(_typePropertyNameUtf8))
            {
                reader = originalReader;
                return false;
            }

            if (!reader.Read())
            {
                throw new JsonException($"Storage payload is missing a '{_typePropertyName}' value.");
            }

            if (reader.TokenType != JsonTokenType.String || !reader.ValueTextEquals(TargetTypeIdentityUtf8))
            {
                reader = originalReader;
                return false;
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName)
            {
                reader = originalReader;
                return false;
            }

            if (GetEnvelopeValuePropertyMatch(ref reader) is EnvelopeValuePropertyMatch.None)
            {
                reader = originalReader;
                return false;
            }

            if (!reader.Read())
            {
                throw new JsonException($"Storage payload is missing a '{_valuePropertyName}' value.");
            }

            state = JsonSerializer.Deserialize<TStateType>(ref reader, options);
            if (!reader.Read())
            {
                throw new JsonException("Storage payload is malformed.");
            }

            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return true;
            }

            // Flattened edge case: the first flattened property name matched the envelope state field name.
            // Reset and let the flattened path consume the payload.
            reader = originalReader;
            state = default;
            return false;
        }

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

            string? sourceIdentity = ParseTypeIdentity(ref reader, out _);
            if (!string.Equals(sourceIdentity, expectedSourceIdentity, StringComparison.Ordinal))
            {
                throw new JsonException("Storage payload type metadata changed while parsing.");
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Storage payload is missing a '{_valuePropertyName}' property.");
            }

            EnvelopeValuePropertyMatch match = GetEnvelopeValuePropertyMatch(ref reader);
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

        private EnvelopeValuePropertyMatch GetEnvelopeValuePropertyMatch(ref Utf8JsonReader reader)
        {
            if (reader.ValueTextEquals(_valuePropertyNameUtf8))
            {
                return EnvelopeValuePropertyMatch.Configured;
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
