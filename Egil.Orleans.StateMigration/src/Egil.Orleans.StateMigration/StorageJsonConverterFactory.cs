using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.StateMigration;

public sealed class StorageJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType
           && typeToConvert.GetGenericTypeDefinition() == typeof(Storage<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type stateType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(StorageJsonConverter<>).MakeGenericType(stateType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class StorageJsonConverter<TStateType> : JsonConverter<Storage<TStateType>>
    {
        public override Storage<TStateType>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotImplementedException("Storage deserialization is implemented in a later phase.");

        public override void Write(Utf8JsonWriter writer, Storage<TStateType> value, JsonSerializerOptions options)
            => throw new NotImplementedException("Storage serialization is implemented in a later phase.");
    }
}
