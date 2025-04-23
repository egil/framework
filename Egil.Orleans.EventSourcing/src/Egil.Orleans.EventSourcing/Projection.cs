using System.Reflection;
using System.Text.Json.Serialization;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Represents a projection of a state that has been derived from a series of events.
/// </summary>
[JsonConverter(typeof(ProjectionJsonConverterFactory))]
public sealed class Projection<TState>
{
    public static int RuntimeMetadataHashCode { get; } = GetTStateMetadataHashCode(typeof(TState));

    [JsonPropertyName("State")]
    public TState? State { get; init; }

    [JsonPropertyName("Version")]
    public int Version { get; init; }

    [JsonPropertyName("MetadataHashCode")]
    public required int MetadataHashCode { get; init; }

    private static int GetTStateMetadataHashCode(Type type)
    {
        var hash = new HashCode();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            hash.Add(property.Name);
            hash.Add(property.PropertyType.FullName);
        }

        return hash.ToHashCode();
    }
}