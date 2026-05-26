using System.Text.Json;
using System.Text.Json.Serialization;

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

        var streamNamespace = ReadRequiredStringProperty(ref reader, nameof(StreamCursor.StreamNamespace));

        ReadRequiredPropertyName(ref reader, nameof(StreamCursor.Token));
        if (!reader.Read())
        {
            throw new JsonException("Unexpected end of StreamCursor token JSON.");
        }

        var token = reader.TokenType is JsonTokenType.Null
            ? null
            : StreamSequenceTokenJsonConverters.Read(ref reader, options);

        string? providerName = null;
        if (!reader.Read())
        {
            throw new JsonException("Unexpected end of StreamCursor JSON.");
        }

        if (reader.TokenType is JsonTokenType.PropertyName)
        {
            if (!reader.ValueTextEquals(nameof(StreamCursor.ProviderName)))
            {
                throw new JsonException($"Expected StreamCursor property '{nameof(StreamCursor.ProviderName)}', got '{reader.GetString()}'.");
            }

            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of StreamCursor provider name JSON.");
            }

            providerName = reader.TokenType is JsonTokenType.Null
                ? null
                : reader.GetString();

            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of StreamCursor JSON.");
            }
        }

        if (reader.TokenType is not JsonTokenType.EndObject)
        {
            throw new JsonException("Expected end of StreamCursor object.");
        }

        return new StreamCursor(streamNamespace, token, providerName);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, StreamCursor value, JsonSerializerOptions options)
    {
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

        writer.WriteEndObject();
    }

    private static string ReadRequiredStringProperty(ref Utf8JsonReader reader, string propertyName)
    {
        ReadRequiredPropertyName(ref reader, propertyName);
        if (!reader.Read() || reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException($"StreamCursor property '{propertyName}' must be a string.");
        }

        return reader.GetString()
            ?? throw new JsonException($"StreamCursor property '{propertyName}' must not be null.");
    }

    private static void ReadRequiredPropertyName(ref Utf8JsonReader reader, string propertyName)
    {
        if (!reader.Read() || reader.TokenType is not JsonTokenType.PropertyName)
        {
            throw new JsonException($"Expected StreamCursor property '{propertyName}'.");
        }

        if (!reader.ValueTextEquals(propertyName))
        {
            throw new JsonException($"Expected StreamCursor property '{propertyName}', got '{reader.GetString()}'.");
        }
    }
}
