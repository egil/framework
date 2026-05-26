using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// STJ converter for <see cref="OutboxSequenceToken"/>. Serializes and
/// deserializes the token's <see cref="OutboxSequenceToken.Sender"/>,
/// <see cref="OutboxSequenceToken.SequenceNumber"/>, and
/// <see cref="OutboxSequenceToken.Epoch"/> properties.
/// </summary>
/// <remarks>
/// <para>
/// Registered on <see cref="OutboxSequenceToken"/> via <c>[JsonConverter]</c>.
/// STJ discovers the attribute automatically — no user-side
/// <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// </remarks>
internal sealed class OutboxSequenceTokenJsonConverter : JsonConverter<OutboxSequenceToken>
{
    /// <inheritdoc/>
    public override OutboxSequenceToken? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.None && !reader.Read())
        {
            throw new JsonException("Unexpected end of outbox sequence token JSON.");
        }

        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType is not JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected outbox sequence token object, got '{reader.TokenType}'.");
        }

        long sequenceNumber = 0;
        var hasSequenceNumber = false;
        GrainId? sender = null;
        DateTimeOffset timestamp = default;
        var hasTimestamp = false;
        DateTimeOffset epoch = default;
        var hasEpoch = false;

        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndObject)
            {
                return new OutboxSequenceToken(
                    hasSequenceNumber ? sequenceNumber : throw new JsonException("Missing SequenceNumber."),
                    sender ?? throw new JsonException("Missing Sender."),
                    hasTimestamp ? timestamp : throw new JsonException("Missing Timestamp."),
                    hasEpoch ? epoch : throw new JsonException("Missing Epoch."));
            }

            if (reader.TokenType is not JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected outbox sequence token property, got '{reader.TokenType}'.");
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of outbox sequence token JSON.");
            }

            switch (propertyName)
            {
                case nameof(OutboxSequenceToken.SequenceNumber):
                    sequenceNumber = reader.GetInt64();
                    hasSequenceNumber = true;
                    break;
                case nameof(OutboxSequenceToken.Sender):
                    sender = reader.TokenType is JsonTokenType.Null
                        ? null
                        : GrainIdJsonConverter.Instance.Read(ref reader, typeof(GrainId), options);
                    break;
                case nameof(OutboxSequenceToken.Timestamp):
                    timestamp = reader.GetDateTimeOffset();
                    hasTimestamp = true;
                    break;
                case nameof(OutboxSequenceToken.Epoch):
                    epoch = reader.GetDateTimeOffset();
                    hasEpoch = true;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unexpected end of outbox sequence token JSON.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, OutboxSequenceToken value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(nameof(OutboxSequenceToken.SequenceNumber), value.SequenceNumber);
        writer.WritePropertyName(nameof(OutboxSequenceToken.Sender));
        GrainIdJsonConverter.Instance.Write(writer, value.Sender, options);
        writer.WriteString(nameof(OutboxSequenceToken.Timestamp), value.Timestamp);
        writer.WriteString(nameof(OutboxSequenceToken.Epoch), value.Epoch);
        writer.WriteEndObject();
    }
}
