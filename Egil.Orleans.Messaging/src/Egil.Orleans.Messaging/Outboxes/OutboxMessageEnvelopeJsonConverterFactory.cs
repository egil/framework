using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// STJ converter factory that creates closed <see cref="JsonConverter{T}"/>
/// instances for <see cref="OutboxMessageEnvelope{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered on <see cref="OutboxMessageEnvelope{T}"/> via
/// <c>[JsonConverter]</c>. STJ discovers the attribute automatically —
/// no user-side <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// </remarks>
internal sealed class OutboxMessageEnvelopeJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsGenericType
            && typeToConvert.GetGenericTypeDefinition() == typeof(OutboxMessageEnvelope<>);
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var messageType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OutboxMessageEnvelopeJsonConverter<>).MakeGenericType(messageType);
        var messageConverter = options.GetConverter(messageType);

        return (JsonConverter?)Activator.CreateInstance(converterType, messageConverter);
    }

    private sealed class OutboxMessageEnvelopeJsonConverter<T>(
        JsonConverter<T> messageConverter) : JsonConverter<OutboxMessageEnvelope<T>>
    {
        public override OutboxMessageEnvelope<T>? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.None && !reader.Read())
            {
                throw new JsonException("Unexpected end of outbox message envelope JSON.");
            }

            if (reader.TokenType is JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType is not JsonTokenType.StartObject)
            {
                throw new JsonException($"Expected outbox message envelope object, got '{reader.TokenType}'.");
            }

            OutboxSequenceToken? token = null;
            T? message = default;
            var hasMessage = false;
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.EndObject)
                {
                    return new OutboxMessageEnvelope<T>(
                        token ?? throw new JsonException("Missing Token."),
                        hasMessage ? message! : throw new JsonException("Missing Message."));
                }

                if (reader.TokenType is not JsonTokenType.PropertyName)
                {
                    throw new JsonException($"Expected outbox message envelope property, got '{reader.TokenType}'.");
                }

                var propertyName = reader.GetString();
                if (!reader.Read())
                {
                    throw new JsonException("Unexpected end of outbox message envelope JSON.");
                }

                switch (propertyName)
                {
                    case nameof(OutboxMessageEnvelope<T>.Token):
                        try
                        {
                            token = JsonSerializer.Deserialize<OutboxSequenceToken>(ref reader, options);
                        }
                        catch (JsonException exception) when (exception.Message == "Missing Sender.")
                        {
                            throw new JsonException("Missing Token.Sender.", exception);
                        }

                        break;
                    case nameof(OutboxMessageEnvelope<T>.Message):
                        message = reader.TokenType is JsonTokenType.Null
                            ? default
                            : messageConverter.Read(ref reader, typeof(T), options);
                        hasMessage = true;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            throw new JsonException("Unexpected end of outbox message envelope JSON.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            OutboxMessageEnvelope<T> value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(OutboxMessageEnvelope<T>.Token));
            JsonSerializer.Serialize(writer, value.Token, options);
            writer.WritePropertyName(nameof(OutboxMessageEnvelope<T>.Message));
            if (value.Message is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                messageConverter.Write(writer, value.Message, options);
            }

            writer.WriteEndObject();
        }
    }
}
