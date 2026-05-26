using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// STJ converter factory that creates closed <see cref="JsonConverter{T}"/>
/// instances for <see cref="Outbox{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered on <see cref="Outbox{T}"/> via <c>[JsonConverter]</c>. STJ
/// discovers the attribute automatically — no user-side
/// <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// <para>
/// The converter serializes only the structural data needed to reconstruct
/// the outbox (sender, epoch, sequence numbers, items). Internal
/// non-restorable service references such as the time provider are excluded.
/// </para>
/// </remarks>
internal sealed class OutboxJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsGenericType
            && typeToConvert.GetGenericTypeDefinition() == typeof(Outbox<>);
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var messageType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OutboxJsonConverter<>).MakeGenericType(messageType);
        var envelopeType = typeof(OutboxMessageEnvelope<>).MakeGenericType(messageType);
        var envelopeConverter = options.GetConverter(envelopeType);

        return (JsonConverter?)Activator.CreateInstance(converterType, envelopeConverter);
    }

    private sealed class OutboxJsonConverter<T>(
        JsonConverter<OutboxMessageEnvelope<T>> envelopeConverter) : JsonConverter<Outbox<T>>
    {
        public override Outbox<T>? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.None && !reader.Read())
            {
                throw new JsonException("Unexpected end of outbox JSON.");
            }

            if (reader.TokenType is JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType is not JsonTokenType.StartObject)
            {
                throw new JsonException($"Expected outbox object, got '{reader.TokenType}'.");
            }

            GrainId? sender = null;
            long latestSequenceNumber = 0;
            var hasLatestSequenceNumber = false;
            DateTimeOffset? epoch = null;
            ImmutableArray<OutboxMessageEnvelope<T>> items = default;
            var hasItems = false;

            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.EndObject)
                {
                    return new Outbox<T>(
                        sender ?? throw new JsonException("Missing Sender."),
                        hasLatestSequenceNumber
                            ? latestSequenceNumber
                            : throw new JsonException("Missing LatestSequenceNumber."),
                        hasItems ? items : throw new JsonException("Missing Items."),
                        epoch);
                }

                if (reader.TokenType is not JsonTokenType.PropertyName)
                {
                    throw new JsonException($"Expected outbox property, got '{reader.TokenType}'.");
                }

                var propertyName = reader.GetString();
                if (!reader.Read())
                {
                    throw new JsonException("Unexpected end of outbox JSON.");
                }

                switch (propertyName)
                {
                    case nameof(Outbox<T>.Sender):
                        sender = reader.TokenType is JsonTokenType.Null
                            ? null
                            : GrainIdJsonConverter.Instance.Read(ref reader, typeof(GrainId), options);
                        break;
                    case nameof(Outbox<T>.LatestSequenceNumber):
                        latestSequenceNumber = reader.GetInt64();
                        hasLatestSequenceNumber = true;
                        break;
                    case nameof(Outbox<T>.Epoch):
                        epoch = reader.TokenType is JsonTokenType.Null
                            ? null
                            : reader.GetDateTimeOffset();
                        break;
                    case "Items":
                        items = ReadItems(ref reader, options);
                        hasItems = true;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            throw new JsonException("Unexpected end of outbox JSON.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            Outbox<T> value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(Outbox<T>.Sender));
            GrainIdJsonConverter.Instance.Write(writer, value.Sender, options);
            writer.WriteNumber(nameof(Outbox<T>.LatestSequenceNumber), value.LatestSequenceNumber);
            writer.WritePropertyName(nameof(Outbox<T>.Epoch));
            if (value.Epoch is { } epoch)
            {
                writer.WriteStringValue(epoch);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WritePropertyName("Items");
            writer.WriteStartArray();
            foreach (var item in value)
            {
                envelopeConverter.Write(writer, item, options);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private ImmutableArray<OutboxMessageEnvelope<T>> ReadItems(
            ref Utf8JsonReader reader,
            JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.Null)
            {
                throw new JsonException("Missing Items.");
            }

            if (reader.TokenType is not JsonTokenType.StartArray)
            {
                throw new JsonException($"Expected outbox items array, got '{reader.TokenType}'.");
            }

            var items = ImmutableArray.CreateBuilder<OutboxMessageEnvelope<T>>();
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.EndArray)
                {
                    return items.ToImmutable();
                }

                if (reader.TokenType is JsonTokenType.Null)
                {
                    throw new JsonException("Missing Item.");
                }

                var item = envelopeConverter.Read(ref reader, typeof(OutboxMessageEnvelope<T>), options);
                items.Add(item ?? throw new JsonException("Missing Item."));
            }

            throw new JsonException("Unexpected end of outbox items JSON.");
        }
    }
}
