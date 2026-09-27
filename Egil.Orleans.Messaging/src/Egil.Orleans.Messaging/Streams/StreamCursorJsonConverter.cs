using System.Text.Json;
using System.Text.Json.Serialization;
using Egil.Orleans.Messaging.Outboxes;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Streams;

/// <summary>
/// STJ converter for <see cref="StreamCursor"/>. Serializes and
/// deserializes the cursor's <see cref="StreamCursor.StreamNamespace"/> and
/// sequence token.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StreamCursor"/> wraps a stream namespace and an optional
/// <c>StreamSequenceToken</c>. The token is polymorphic and this converter
/// delegates concrete token payloads to <see cref="StreamSequenceTokenJsonConverters"/>.
/// </para>
/// <para>
/// Registered on <see cref="StreamCursor"/> via <c>[JsonConverter]</c>.
/// STJ discovers the attribute automatically — no user-side
/// <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// </remarks>
internal sealed class StreamCursorJsonConverter : JsonConverter<StreamCursor>
{
    /// <inheritdoc/>
    public override StreamCursor? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.None && !reader.Read())
        {
            throw new JsonException("Unexpected end of StreamCursor JSON.");
        }

        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType is not JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected StreamCursor object, got '{reader.TokenType}'.");
        }

        string? streamNamespace = null;
        var hasStreamNamespace = false;
        StreamSequenceToken? token = null;
        var hasToken = false;
        string? providerName = null;
        var hasProviderName = false;
        StreamId? streamId = null;
        OutboxSequenceToken? identity = null;
        var hasStreamId = false;
        var hasIdentity = false;
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndObject)
            {
                var result = new StreamCursor(
                    hasStreamNamespace
                        ? streamNamespace!
                        : throw new JsonException($"Missing StreamCursor property '{nameof(StreamCursor.StreamNamespace)}'."),
                    hasToken
                        ? token
                        : throw new JsonException($"Missing StreamCursor property '{nameof(StreamCursor.Token)}'."),
                    providerName) { StreamId = streamId, OutboxToken = identity };
                result.ValidateSource();
                return result;
            }

            if (reader.TokenType is not JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected StreamCursor property, got '{reader.TokenType}'.");
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of StreamCursor JSON.");
            }

            switch (propertyName)
            {
                case nameof(StreamCursor.StreamNamespace):
                    ThrowIfDuplicate(hasStreamNamespace, nameof(StreamCursor.StreamNamespace));
                    if (reader.TokenType is not JsonTokenType.String)
                    {
                        throw new JsonException(
                            $"StreamCursor property '{nameof(StreamCursor.StreamNamespace)}' must be a string.");
                    }

                    streamNamespace = reader.GetString()
                        ?? throw new JsonException(
                            $"StreamCursor property '{nameof(StreamCursor.StreamNamespace)}' must not be null.");
                    hasStreamNamespace = true;
                    break;
                case nameof(StreamCursor.Token):
                    ThrowIfDuplicate(hasToken, nameof(StreamCursor.Token));
                    token = reader.TokenType is JsonTokenType.Null
                        ? null
                        : StreamSequenceTokenJsonConverters.Read(ref reader, options);
                    hasToken = true;
                    break;
                case nameof(StreamCursor.ProviderName):
                    ThrowIfDuplicate(hasProviderName, nameof(StreamCursor.ProviderName));
                    if (reader.TokenType is not (JsonTokenType.Null or JsonTokenType.String))
                    {
                        throw new JsonException(
                            $"StreamCursor property '{nameof(StreamCursor.ProviderName)}' must be a string or null.");
                    }

                    providerName = reader.TokenType is JsonTokenType.Null
                        ? null
                        : reader.GetString();
                    hasProviderName = true;
                    break;
                case nameof(StreamCursor.StreamId):
                    ThrowIfDuplicate(hasStreamId, nameof(StreamCursor.StreamId));
                    streamId = JsonSerializer.Deserialize<StreamIdJsonModel>(ref reader, options)?.ToStreamId();
                    hasStreamId = true;
                    break;
                case nameof(StreamCursor.OutboxToken):
                    ThrowIfDuplicate(hasIdentity, nameof(StreamCursor.OutboxToken));
                    identity = JsonSerializer.Deserialize<OutboxSequenceToken>(ref reader, options);
                    hasIdentity = true;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unexpected end of StreamCursor JSON.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, StreamCursor value, JsonSerializerOptions options)
    {
        value.ValidateSource();
        writer.WriteStartObject();
        writer.WriteString(nameof(StreamCursor.StreamNamespace), value.StreamNamespace);
        writer.WritePropertyName(nameof(StreamCursor.Token));
        if (value.Token is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            StreamSequenceTokenJsonConverters.Write(writer, value.Token, options);
        }

        if (value.ProviderName is not null)
        {
            writer.WriteString(nameof(StreamCursor.ProviderName), value.ProviderName);
        }

        if (value.StreamId is { } id)
        {
            writer.WritePropertyName(nameof(StreamCursor.StreamId));
            JsonSerializer.Serialize(writer, StreamIdJsonModel.From(id), options);
        }
        if (value.OutboxToken is { } identity)
        {
            writer.WritePropertyName(nameof(StreamCursor.OutboxToken));
            JsonSerializer.Serialize(writer, identity, options);
        }
        writer.WriteEndObject();
    }

    private static void ThrowIfDuplicate(bool hasProperty, string propertyName)
    {
        if (hasProperty)
        {
            throw new JsonException($"Duplicate StreamCursor property '{propertyName}'.");
        }
    }
}
