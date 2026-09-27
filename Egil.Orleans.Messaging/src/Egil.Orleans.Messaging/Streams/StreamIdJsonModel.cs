using System.Text.Json.Serialization;
using System.Text.Json;

namespace Egil.Orleans.Messaging.Streams;

// Persist bytes rather than display strings: stream keys need not be valid UTF-8.
internal sealed record StreamIdJsonModel(
    [property: JsonPropertyName("Namespace"), JsonRequired] byte[] Namespace,
    [property: JsonPropertyName("Key"), JsonRequired] byte[] Key)
{
    internal static StreamIdJsonModel From(StreamId id) => new(id.Namespace.ToArray(), id.Key.ToArray());
    internal StreamId ToStreamId()
    {
        if (Namespace is null || Namespace.Length == 0 || Key is null)
            throw new JsonException("Full stream identity requires namespace and key bytes.");
        return StreamId.Create(Namespace, Key);
    }
}
