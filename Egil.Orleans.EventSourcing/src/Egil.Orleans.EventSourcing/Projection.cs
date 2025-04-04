using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Represents a projection of a state that has been derived from a series of events.
/// </summary>
[JsonConverter(typeof(ProjectionJsonConverterFactory))]
public sealed class Projection<TState>
{
    [JsonPropertyName("State")]
    public TState? State { get; init; }

    [JsonPropertyName("Version")]
    public int Version { get; init; }

    [JsonPropertyName("MetadataHashCode")]
    public required int MetadataHashCode { get; init; }
}

public sealed class ProjectionJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Projection<>);

    public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
    {
        Type stateType = type.GetGenericArguments()[0];
        Type converterType = typeof(ProjectionJsonConverterInner<>).MakeGenericType(stateType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

internal sealed class ProjectionJsonConverterInner<TState> : JsonConverter<Projection<TState>>
{
    private const string StatePropertyName = "State";
    private const string VersionPropertyName = "Version";
    private const string MetadataHashCodePropertyName = "MetadataHashCode";

    public override Projection<TState>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        TState? state = default;
        int version = 0;
        int metadataHashCode = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return new Projection<TState>
                {
                    State = state,
                    Version = version,
                    MetadataHashCode = metadataHashCode
                };

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();

            string propertyName = reader.GetString()!;
            reader.Read();

            switch (propertyName)
            {
                case StatePropertyName:
                    state = JsonSerializer.Deserialize<TState>(ref reader, options);
                    break;
                case VersionPropertyName:
                    version = reader.GetInt32();
                    break;
                case MetadataHashCodePropertyName:
                    metadataHashCode = reader.GetInt32();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Projection<TState> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName(StatePropertyName);
        JsonSerializer.Serialize(writer, value.State, options);
        writer.WriteNumber(VersionPropertyName, value.Version);
        writer.WriteNumber(MetadataHashCodePropertyName, value.MetadataHashCode);
        writer.WriteEndObject();
    }
}