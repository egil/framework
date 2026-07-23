using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans.Streaming.EventHubs;

namespace Egil.Orleans.Messaging.Streams.EventHubs;

internal static class EventHubStreamSequenceTokenJsonConverters
{
    public static void Register()
    {
        StreamSequenceTokenJsonConverters.Register(
            "orleans.event-hubs.sequence-token",
            new EventHubSequenceTokenJsonConverter());
        StreamSequenceTokenJsonConverters.Register(
            "orleans.event-hubs.sequence-token-v2",
            new EventHubSequenceTokenV2JsonConverter());
        StreamSequenceTokenJsonConverters.Register(
            EnrichedEventHubSequenceToken.TypeAlias,
            new EnrichedEventHubSequenceTokenJsonConverter());
    }
}

/// <summary>
/// JSON converter for Orleans Event Hubs sequence tokens.
/// </summary>
public sealed class EventHubSequenceTokenJsonConverter : JsonConverter<EventHubSequenceToken>
{
    /// <inheritdoc/>
    public override EventHubSequenceToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var properties = EventHubSequenceTokenJsonConverterHelpers.ReadProperties(
            ref reader,
            nameof(EventHubSequenceToken),
            requireEnrichedProperties: false);

        return new EventHubSequenceToken(
            properties.EventHubOffset,
            properties.SequenceNumber,
            properties.EventIndex);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EventHubSequenceToken value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        EventHubSequenceTokenJsonConverterHelpers.WriteBaseProperties(
            writer,
            value.EventHubOffset,
            value.SequenceNumber,
            value.EventIndex);
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSON converter for Orleans Event Hubs V2 sequence tokens.
/// </summary>
public sealed class EventHubSequenceTokenV2JsonConverter : JsonConverter<EventHubSequenceTokenV2>
{
    /// <inheritdoc/>
    public override EventHubSequenceTokenV2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var properties = EventHubSequenceTokenJsonConverterHelpers.ReadProperties(
            ref reader,
            nameof(EventHubSequenceTokenV2),
            requireEnrichedProperties: false);

        return new EventHubSequenceTokenV2(
            properties.EventHubOffset,
            properties.SequenceNumber,
            properties.EventIndex);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EventHubSequenceTokenV2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        EventHubSequenceTokenJsonConverterHelpers.WriteBaseProperties(
            writer,
            value.EventHubOffset,
            value.SequenceNumber,
            value.EventIndex);
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSON converter for <see cref="EnrichedEventHubSequenceToken"/>.
/// </summary>
public sealed class EnrichedEventHubSequenceTokenJsonConverter : JsonConverter<EnrichedEventHubSequenceToken>
{
    /// <inheritdoc/>
    public override EnrichedEventHubSequenceToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var properties = EventHubSequenceTokenJsonConverterHelpers.ReadProperties(
            ref reader,
            nameof(EnrichedEventHubSequenceToken),
            requireEnrichedProperties: true);

        return new EnrichedEventHubSequenceToken(
            properties.EventHubOffset,
            properties.SequenceNumber,
            properties.EventIndex,
            properties.EnqueuedTime,
            properties.ProviderName!,
            properties.TraceParent);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EnrichedEventHubSequenceToken value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        EventHubSequenceTokenJsonConverterHelpers.WriteBaseProperties(
            writer,
            value.EventHubOffset,
            value.SequenceNumber,
            value.EventIndex);
        writer.WriteString("EnqueuedTime", value.EnqueuedTime);
        writer.WriteString("ProviderName", value.ProviderName);
        writer.WriteString("TraceParent", value.TraceParent);
        writer.WriteEndObject();
    }
}

internal static class EventHubSequenceTokenJsonConverterHelpers
{
    public static void WriteBaseProperties(
        Utf8JsonWriter writer,
        string eventHubOffset,
        long sequenceNumber,
        int eventIndex)
    {
        writer.WriteString("EventHubOffset", eventHubOffset);
        writer.WriteNumber("SequenceNumber", sequenceNumber);
        writer.WriteNumber("EventIndex", eventIndex);
    }

    public static Properties ReadProperties(
        ref Utf8JsonReader reader,
        string typeName,
        bool requireEnrichedProperties)
    {
        if (reader.TokenType is not JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected {typeName} object, got '{reader.TokenType}'.");
        }

        string? eventHubOffset = null;
        var hasEventHubOffset = false;
        long sequenceNumber = 0;
        var hasSequenceNumber = false;
        var eventIndex = 0;
        var hasEventIndex = false;
        DateTimeOffset enqueuedTime = default;
        var hasEnqueuedTime = false;
        string? providerName = null;
        var hasProviderName = false;
        string? traceParent = null;
        var hasTraceParent = false;

        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndObject)
            {
                return new Properties(
                    hasEventHubOffset
                        ? eventHubOffset!
                        : throw new JsonException($"Missing {typeName} property 'EventHubOffset'."),
                    hasSequenceNumber
                        ? sequenceNumber
                        : throw new JsonException($"Missing {typeName} property 'SequenceNumber'."),
                    hasEventIndex
                        ? eventIndex
                        : throw new JsonException($"Missing {typeName} property 'EventIndex'."),
                    !requireEnrichedProperties || hasEnqueuedTime
                        ? enqueuedTime
                        : throw new JsonException($"Missing {typeName} property 'EnqueuedTime'."),
                    !requireEnrichedProperties || hasProviderName
                        ? providerName
                        : throw new JsonException($"Missing {typeName} property 'ProviderName'."),
                    !requireEnrichedProperties || hasTraceParent
                        ? traceParent
                        : throw new JsonException($"Missing {typeName} property 'TraceParent'."));
            }

            if (reader.TokenType is not JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected {typeName} property, got '{reader.TokenType}'.");
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException($"Unexpected end of {typeName} JSON.");
            }

            switch (propertyName)
            {
                case "EventHubOffset":
                    ThrowIfDuplicate(hasEventHubOffset, typeName, propertyName);
                    if (reader.TokenType is not JsonTokenType.String)
                    {
                        throw new JsonException("Property 'EventHubOffset' must be a string.");
                    }

                    eventHubOffset = reader.GetString()
                        ?? throw new JsonException("Property 'EventHubOffset' must not be null.");
                    hasEventHubOffset = true;
                    break;
                case "SequenceNumber":
                    ThrowIfDuplicate(hasSequenceNumber, typeName, propertyName);
                    if (reader.TokenType is not JsonTokenType.Number
                        || !reader.TryGetInt64(out sequenceNumber))
                    {
                        throw new JsonException("Property 'SequenceNumber' must be a 64-bit integer.");
                    }

                    hasSequenceNumber = true;
                    break;
                case "EventIndex":
                    ThrowIfDuplicate(hasEventIndex, typeName, propertyName);
                    if (reader.TokenType is not JsonTokenType.Number
                        || !reader.TryGetInt32(out eventIndex))
                    {
                        throw new JsonException("Property 'EventIndex' must be a 32-bit integer.");
                    }

                    hasEventIndex = true;
                    break;
                case "EnqueuedTime" when requireEnrichedProperties:
                    ThrowIfDuplicate(hasEnqueuedTime, typeName, propertyName);
                    if (reader.TokenType is not JsonTokenType.String
                        || !reader.TryGetDateTimeOffset(out enqueuedTime))
                    {
                        throw new JsonException("Property 'EnqueuedTime' must be a date-time string.");
                    }

                    hasEnqueuedTime = true;
                    break;
                case "ProviderName" when requireEnrichedProperties:
                    ThrowIfDuplicate(hasProviderName, typeName, propertyName);
                    if (reader.TokenType is not JsonTokenType.String)
                    {
                        throw new JsonException("Property 'ProviderName' must be a string.");
                    }

                    providerName = reader.GetString()
                        ?? throw new JsonException("Property 'ProviderName' must not be null.");
                    hasProviderName = true;
                    break;
                case "TraceParent" when requireEnrichedProperties:
                    ThrowIfDuplicate(hasTraceParent, typeName, propertyName);
                    if (reader.TokenType is not (JsonTokenType.Null or JsonTokenType.String))
                    {
                        throw new JsonException("Property 'TraceParent' must be a string or null.");
                    }

                    traceParent = reader.TokenType is JsonTokenType.Null
                        ? null
                        : reader.GetString();
                    hasTraceParent = true;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException($"Unexpected end of {typeName} JSON.");
    }

    private static void ThrowIfDuplicate(bool hasProperty, string typeName, string? propertyName)
    {
        if (hasProperty)
        {
            throw new JsonException($"Duplicate {typeName} property '{propertyName}'.");
        }
    }

    public readonly record struct Properties(
        string EventHubOffset,
        long SequenceNumber,
        int EventIndex,
        DateTimeOffset EnqueuedTime,
        string? ProviderName,
        string? TraceParent);
}
