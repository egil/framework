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

        return (JsonConverter?)Activator.CreateInstance(converterType);
    }

    private sealed class OutboxMessageEnvelopeJsonConverter<T> : JsonConverter<OutboxMessageEnvelope<T>>
    {
        public override OutboxMessageEnvelope<T>? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var model = JsonSerializer.Deserialize<OutboxMessageEnvelopeJsonModel<T>>(ref reader, options);
            if (model is null)
            {
                return null;
            }

            if (model.Token is null)
            {
                throw new JsonException("Missing Token.");
            }

            return new OutboxMessageEnvelope<T>(model.Token, model.Message);
        }

        public override void Write(
            Utf8JsonWriter writer,
            OutboxMessageEnvelope<T> value,
            JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(
                writer,
                new OutboxMessageEnvelopeJsonModel<T>(value.Token, value.Message),
                options);
        }
    }

    private sealed record OutboxMessageEnvelopeJsonModel<T>(
        OutboxSequenceToken? Token,
        T Message);
}