using Orleans.Storage;
using System.Text.Json;

namespace Egil.Orleans.EventSourcing;

public sealed class SystemTextJsonGrainStorageSerializer(JsonSerializerOptions? options = null) : IGrainStorageSerializer
{
    private readonly JsonSerializerOptions options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public T? Deserialize<T>(BinaryData input)
    {
        var result = input.ToObjectFromJson<T>(options);
        return result;
    }

    public BinaryData Serialize<T>(T input)
    {
        var result = BinaryData.FromObjectAsJson(input, options);
        return result;
    }
}

