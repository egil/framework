using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Egil.Orleans.StateMigration.SystemTextJson;

internal sealed class EnvelopedStorageJsonConverter<TStateType> : JsonConverter<Storage<TStateType>>
{
    private static readonly string TargetTypeIdentity = StateTypeIdentity.GetIdentity(typeof(TStateType));
    private static readonly byte[] TargetTypeIdentityUtf8 = Encoding.UTF8.GetBytes(TargetTypeIdentity);
    private readonly string _typePropertyName;
    private readonly string _valuePropertyName;
    private readonly byte[] _typePropertyNameUtf8;
    private readonly byte[] _valuePropertyNameUtf8;
    // Cache metadata once per converter instance to avoid repeated resolver lookups on each operation.
    private readonly JsonTypeInfo<TStateType> _stateTypeInfo;

    public EnvelopedStorageJsonConverter(
        string typePropertyName,
        string valuePropertyName,
        JsonSerializerOptions options)
    {
        _typePropertyName = typePropertyName;
        _valuePropertyName = valuePropertyName;
        _typePropertyNameUtf8 = Encoding.UTF8.GetBytes(typePropertyName);
        _valuePropertyNameUtf8 = Encoding.UTF8.GetBytes(valuePropertyName);
        _stateTypeInfo = (JsonTypeInfo<TStateType>)options.GetTypeInfo(typeof(TStateType));
    }

    public override Storage<TStateType>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            // Legacy payloads for collection/primitive states are often non-object JSON (for example arrays, numbers, strings).
            return DeserializeLegacyPayload(ref reader);
        }

        // Most payloads are expected to already be current type + current envelope shape.
        // This method keeps the original reader untouched on a miss, so we can fall back safely.
        if (TryDeserializeCurrentEnvelopedPayload(ref reader, out Storage<TStateType>? currentEnvelopedPayload))
        {
            return currentEnvelopedPayload;
        }

        var probe = reader;
        if (!probe.Read() || probe.TokenType == JsonTokenType.EndObject)
        {
            return DeserializeLegacyPayload(ref reader);
        }

        if (probe.TokenType != JsonTokenType.PropertyName)
        {
            return DeserializeLegacyPayload(ref reader);
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
                // Hot path optimization: current type identity matched via UTF-8 byte comparison above.
                // Continue parsing from this probe and commit reader position only on successful envelope parse.
                var envelopeProbe = probe;
                if (envelopeProbe.Read() && envelopeProbe.TokenType == JsonTokenType.PropertyName)
                {
                    if (envelopeProbe.ValueTextEquals(_valuePropertyNameUtf8)
                        && TryDeserializeCurrentEnvelopedStateAfterValueProperty(
                            ref envelopeProbe,
                            out TStateType? envelopedState))
                    {
                        if (envelopedState is null)
                        {
                            throw new JsonException("Storage payload produced a null state for the current type.");
                        }

                        reader = envelopeProbe;
                        return new Storage<TStateType>
                        {
                            Value = InvokeOnDeserializedCallback(envelopedState),
                            // Rewrite when incoming payload shape differs from configured output.
                            MigratedDuringDeserialization = false,
                        };
                    }

                    if (envelopeProbe.ValueTextEquals("value"u8))
                    {
                        // Reject legacy/default "$value" when user configured a different field name.
                        throw new JsonException(
                            $"Storage payload must use '{_valuePropertyName}' as the state property name.");
                    }
                }

                // Fallback: flattened payload (or edge-case object where first flattened property is named like the
                // configured envelope value property). In enveloped mode this is treated as migrated so callers can rewrite.
                TStateType? state = DeserializeCurrentFlattenedPayload(ref reader);
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

            bool isEnvelopedPayload = ProbeEnvelopedPayload(ref probe);
            JsonTypeInfo sourceTypeInfo = options.GetTypeInfo(sourceType);
            object? source = isEnvelopedPayload
                ? DeserializeEnvelopedState(ref reader, sourceIdentity, sourceType, options)
                : JsonSerializer.Deserialize(ref reader, sourceTypeInfo);
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

        return DeserializeLegacyPayload(ref reader);
    }

    public override void Write(Utf8JsonWriter writer, Storage<TStateType> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(_typePropertyName, TargetTypeIdentity);
        writer.WritePropertyName(_valuePropertyName);
        // Envelope payload avoids JsonElement materialization and supports any JSON shape for TStateType.
        JsonSerializer.Serialize(writer, value.Value, _stateTypeInfo);
        writer.WriteEndObject();
    }

    private bool TryDeserializeCurrentEnvelopedPayload(
        ref Utf8JsonReader reader,
        out Storage<TStateType>? payload)
    {
        payload = default;

        var probe = reader;
        if (!probe.Read() || probe.TokenType != JsonTokenType.PropertyName || !probe.ValueTextEquals(_typePropertyNameUtf8))
        {
            return false;
        }

        if (!probe.Read())
        {
            throw new JsonException($"Storage payload is missing a '{_typePropertyName}' value.");
        }

        if (probe.TokenType != JsonTokenType.String || !probe.ValueTextEquals(TargetTypeIdentityUtf8))
        {
            return false;
        }

        if (!probe.Read() || probe.TokenType != JsonTokenType.PropertyName)
        {
            return false;
        }

        if (!probe.ValueTextEquals(_valuePropertyNameUtf8))
        {
            if (probe.ValueTextEquals("value"u8))
            {
                throw new JsonException($"Storage payload must use '{_valuePropertyName}' as the state property name.");
            }

            return false;
        }

        if (!TryDeserializeCurrentEnvelopedStateAfterValueProperty(ref probe, out TStateType? state))
        {
            // Payload may be flattened with the first state property named like the envelope value property.
            // Leave caller to handle full fallback path.
            return false;
        }

        if (state is null)
        {
            throw new JsonException("Storage payload produced a null state for the current type.");
        }

        reader = probe;
        payload = new Storage<TStateType>
        {
            Value = InvokeOnDeserializedCallback(state),
            MigratedDuringDeserialization = false,
        };
        return true;
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

    private bool ProbeEnvelopedPayload(ref Utf8JsonReader probe)
    {
        // Cheap shape check used only to choose parser path for non-current source types.
        // We validate: "$value": <json>, and exactly end-of-object after that value.
        if (!probe.Read() || probe.TokenType != JsonTokenType.PropertyName)
        {
            return false;
        }

        if (!probe.ValueTextEquals(_valuePropertyNameUtf8))
        {
            return false;
        }

        if (!probe.Read())
        {
            throw new JsonException($"Storage envelope payload is missing a '{_valuePropertyName}' value.");
        }

        probe.Skip();
        return probe.Read() && probe.TokenType == JsonTokenType.EndObject
            ? true
            : false;
    }

    private TStateType? DeserializeCurrentFlattenedPayload(ref Utf8JsonReader reader)
        => JsonSerializer.Deserialize(ref reader, _stateTypeInfo);

    private bool TryDeserializeCurrentEnvelopedStateAfterValueProperty(
        ref Utf8JsonReader reader,
        out TStateType? state)
    {
        if (!reader.Read())
        {
            throw new JsonException($"Storage payload is missing a '{_valuePropertyName}' value.");
        }

        state = JsonSerializer.Deserialize(ref reader, _stateTypeInfo);
        if (!reader.Read())
        {
            throw new JsonException("Storage payload is malformed.");
        }

        if (reader.TokenType == JsonTokenType.EndObject)
        {
            return true;
        }

        // Flattened edge case: first flattened property name matched configured envelope state field name.
        // We only treat this as envelope when "$value" is the final property.
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

        if (!reader.ValueTextEquals(_valuePropertyNameUtf8))
        {
            throw new JsonException(
                $"Storage payload must use '{_valuePropertyName}' as the state property name.");
        }

        if (!reader.Read())
        {
            throw new JsonException($"Storage payload is missing a '{_valuePropertyName}' value.");
        }

        JsonTypeInfo sourceTypeInfo = options.GetTypeInfo(stateType);
        object? state = JsonSerializer.Deserialize(ref reader, sourceTypeInfo);
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
        {
            throw new JsonException("Storage envelope payload contains unexpected properties.");
        }

        return state;
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

    private Storage<TStateType> DeserializeLegacyPayload(ref Utf8JsonReader reader)
    {
        // Missing leading $type means legacy/unversioned payload. Mark migrated so callers can persist versioned format.
        TStateType? legacyState = JsonSerializer.Deserialize(ref reader, _stateTypeInfo);
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
