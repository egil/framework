using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.EventSourcing;

public sealed class ProjectionJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Projection<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type stateType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(ProjectionJsonConverterInner<>).MakeGenericType(stateType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class ProjectionJsonConverterInner<TState> : JsonConverter<Projection<TState>>
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
                {
                    return new Projection<TState>
                    {
                        State = state,
                        Version = state is not null ? version : 0,
                        MetadataHashCode = state is not null ? metadataHashCode : 0,
                    };
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException();
                }

                string propertyName = reader.GetString()!;
                reader.Read();

                switch (propertyName)
                {
                    case StatePropertyName:
                        try
                        {
                            state = JsonSerializer.Deserialize<TState>(ref reader, options);
                        }
                        catch (JsonException)
                        {
                            reader.Skip();
                            state = default;
                        }
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
            writer.WriteNumber(VersionPropertyName, value.Version);
            writer.WriteNumber(MetadataHashCodePropertyName, value.MetadataHashCode);
            writer.WritePropertyName(StatePropertyName);
            JsonSerializer.Serialize(writer, value.State, options);
            writer.WriteEndObject();
        }
    }
}
