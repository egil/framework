using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging.Outboxes;

internal sealed class GrainIdJsonConverter : JsonConverter<GrainId>
{
    public static GrainIdJsonConverter Instance { get; } = new();

    public override GrainId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected grain id object, got '{reader.TokenType}'.");
        }

        string? type = null;
        string? key = null;
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndObject)
            {
                return GrainId.Create(
                    type ?? throw new JsonException("Missing Type."),
                    key ?? throw new JsonException("Missing Key."));
            }

            if (reader.TokenType is not JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected grain id property, got '{reader.TokenType}'.");
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of grain id JSON.");
            }

            switch (propertyName)
            {
                case "Type":
                    type = reader.GetString();
                    break;
                case "Key":
                    key = reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unexpected end of grain id JSON.");
    }

    public override void Write(Utf8JsonWriter writer, GrainId value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Type", value.Type.ToString());
        writer.WriteString("Key", value.Key.ToString());
        writer.WriteEndObject();
    }
}