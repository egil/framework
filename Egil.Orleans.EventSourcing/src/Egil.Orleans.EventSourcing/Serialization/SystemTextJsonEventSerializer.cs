using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.EventSourcing.Serialization;

/// <summary>
/// System.Text.Json-based event serializer.
/// Supports polymorphic serialization using JsonDerivedType attributes.
/// </summary>
/// <typeparam name="TEvent">The base event type</typeparam>
public sealed class SystemTextJsonEventSerializer<TEvent> : IEventSerializer<TEvent>
    where TEvent : class
{
    private readonly JsonSerializerOptions options;

    /// <summary>
    /// Initializes a new instance of SystemTextJsonEventSerializer.
    /// </summary>
    /// <param name="options">JSON serialization options</param>
    public SystemTextJsonEventSerializer(JsonSerializerOptions? options = null)
    {
        this.options = options ?? CreateDefaultOptions();
    }

    /// <summary>
    /// Serializes an event to bytes.
    /// </summary>
    public byte[] Serialize(TEvent @event)
    {
        return JsonSerializer.SerializeToUtf8Bytes(@event, typeof(TEvent), options);
    }

    /// <summary>
    /// Deserializes bytes to an event.
    /// </summary>
    public TEvent Deserialize(byte[] data, string eventTypeName)
    {
        var result = JsonSerializer.Deserialize<TEvent>(data, options);
        return result ?? throw new InvalidOperationException($"Failed to deserialize event of type {eventTypeName}");
    }

    /// <summary>
    /// Gets the type name for an event.
    /// Uses the JsonDerivedType discriminator if available, otherwise the full type name.
    /// </summary>
    public string GetEventTypeName(TEvent @event)
    {
        var eventType = @event.GetType();
        
        // Check if the base type has JsonDerivedType attributes
        var baseType = typeof(TEvent);
        var derivedTypeAttributes = baseType.GetCustomAttributes(typeof(JsonDerivedTypeAttribute), true)
            .Cast<JsonDerivedTypeAttribute>();

        foreach (var attr in derivedTypeAttributes)
        {
            if (attr.DerivedType == eventType && attr.TypeDiscriminator is string discriminator)
            {
                return discriminator;
            }
        }

        // Fall back to full type name
        return eventType.FullName ?? eventType.Name;
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
    }
}

/// <summary>
/// Factory for creating SystemTextJsonEventSerializer instances.
/// </summary>
public static class SystemTextJsonEventSerializerFactory
{
    /// <summary>
    /// Creates a serializer with default options.
    /// </summary>
    public static SystemTextJsonEventSerializer<TEvent> Create<TEvent>()
        where TEvent : class
    {
        return new SystemTextJsonEventSerializer<TEvent>();
    }

    /// <summary>
    /// Creates a serializer with custom options.
    /// </summary>
    public static SystemTextJsonEventSerializer<TEvent> Create<TEvent>(JsonSerializerOptions options)
        where TEvent : class
    {
        return new SystemTextJsonEventSerializer<TEvent>(options);
    }

    /// <summary>
    /// Creates a serializer with polymorphic support configured for the specified event types.
    /// </summary>
    public static SystemTextJsonEventSerializer<TEvent> CreatePolymorphic<TEvent>(
        params (Type eventType, string discriminator)[] eventTypes)
        where TEvent : class
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        // Add type discriminator resolver for polymorphic serialization
        var typeInfoResolver = new DefaultJsonTypeInfoResolver();
        
        typeInfoResolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type == typeof(TEvent))
            {
                typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
                {
                    TypeDiscriminatorPropertyName = "$type",
                    IgnoreUnrecognizedTypeDiscriminators = false,
                    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
                };

                foreach (var (eventType, discriminator) in eventTypes)
                {
                    typeInfo.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(eventType, discriminator));
                }
            }
        });

        options.TypeInfoResolver = typeInfoResolver;
        
        return new SystemTextJsonEventSerializer<TEvent>(options);
    }
}
