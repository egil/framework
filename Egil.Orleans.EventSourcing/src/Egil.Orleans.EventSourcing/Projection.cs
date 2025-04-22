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