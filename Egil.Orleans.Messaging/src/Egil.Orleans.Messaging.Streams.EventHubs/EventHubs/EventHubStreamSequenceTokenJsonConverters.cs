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
        EventHubSequenceTokenJsonConverterHelpers.ExpectStartObject(ref reader, nameof(EventHubSequenceToken));
        var offset = EventHubSequenceTokenJsonConverterHelpers.ReadRequiredStringProperty(ref reader, "EventHubOffset");
        var sequenceNumber = EventHubSequenceTokenJsonConverterHelpers.ReadRequiredInt64Property(ref reader, "SequenceNumber");
        var eventIndex = EventHubSequenceTokenJsonConverterHelpers.ReadRequiredInt32Property(ref reader, "EventIndex");
        EventHubSequenceTokenJsonConverterHelpers.ReadEndObject(ref reader);

        return new EventHubSequenceToken(offset, sequenceNumber, eventIndex);
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
        EventHubSequenceTokenJsonConverterHelpers.ExpectStartObject(ref reader, nameof(EventHubSequenceTokenV2));
        var offset = EventHubSequenceTokenJsonConverterHelpers.ReadRequiredStringProperty(ref reader, "EventHubOffset");
        var sequenceNumber = EventHubSequenceTokenJsonConverterHelpers.ReadRequiredInt64Property(ref reader, "SequenceNumber");
        var eventIndex = EventHubSequenceTokenJsonConverterHelpers.ReadRequiredInt32Property(ref reader, "EventIndex");
        EventHubSequenceTokenJsonConverterHelpers.ReadEndObject(ref reader);

        return new EventHubSequenceTokenV2(offset, sequenceNumber, eventIndex);
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
        EventHubSequenceTokenJsonConverterHelpers.ExpectStartObject(ref reader, nameof(EnrichedEventHubSequenceToken));
        var offset = EventHubSequenceTokenJsonConverterHelpers.ReadRequiredStringProperty(ref reader, "EventHubOffset");
        var sequenceNumber = EventHubSequenceTokenJsonConverterHelpers.ReadRequiredInt64Property(ref reader, "SequenceNumber");
        var eventIndex = EventHubSequenceTokenJsonConverterHelpers.ReadRequiredInt32Property(ref reader, "EventIndex");
        var enqueuedTime = EventHubSequenceTokenJsonConverterHelpers.ReadRequiredDateTimeOffsetProperty(ref reader, "EnqueuedTime");
        var providerName = EventHubSequenceTokenJsonConverterHelpers.ReadRequiredStringProperty(ref reader, "ProviderName");
        var traceParent = EventHubSequenceTokenJsonConverterHelpers.ReadNullableStringProperty(ref reader, "TraceParent");
        EventHubSequenceTokenJsonConverterHelpers.ReadEndObject(ref reader);

        return new EnrichedEventHubSequenceToken(
            offset,
            sequenceNumber,
            eventIndex,
            enqueuedTime,
            providerName,
            traceParent);
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
    public static void ExpectStartObject(ref Utf8JsonReader reader, string typeName)
    {
        if (reader.TokenType is not JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected {typeName} object, got '{reader.TokenType}'.");
        }
    }

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

    public static string ReadRequiredStringProperty(ref Utf8JsonReader reader, string propertyName)
    {
        ReadRequiredPropertyName(ref reader, propertyName);
        if (!reader.Read() || reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException($"Property '{propertyName}' must be a string.");
        }

        return reader.GetString()
            ?? throw new JsonException($"Property '{propertyName}' must not be null.");
    }

    public static string? ReadNullableStringProperty(ref Utf8JsonReader reader, string propertyName)
    {
        ReadRequiredPropertyName(ref reader, propertyName);
        if (!reader.Read())
        {
            throw new JsonException($"Unexpected end of JSON while reading '{propertyName}'.");
        }

        return reader.TokenType is JsonTokenType.Null
            ? null
            : reader.GetString();
    }

    public static long ReadRequiredInt64Property(ref Utf8JsonReader reader, string propertyName)
    {
        ReadRequiredPropertyName(ref reader, propertyName);
        if (!reader.Read() || reader.TokenType is not JsonTokenType.Number)
        {
            throw new JsonException($"Property '{propertyName}' must be a number.");
        }

        return reader.GetInt64();
    }

    public static int ReadRequiredInt32Property(ref Utf8JsonReader reader, string propertyName)
    {
        ReadRequiredPropertyName(ref reader, propertyName);
        if (!reader.Read() || reader.TokenType is not JsonTokenType.Number)
        {
            throw new JsonException($"Property '{propertyName}' must be a number.");
        }

        return reader.GetInt32();
    }

    public static DateTimeOffset ReadRequiredDateTimeOffsetProperty(ref Utf8JsonReader reader, string propertyName)
    {
        ReadRequiredPropertyName(ref reader, propertyName);
        if (!reader.Read() || reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException($"Property '{propertyName}' must be a date-time string.");
        }

        return reader.GetDateTimeOffset();
    }

    public static void ReadEndObject(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType is not JsonTokenType.EndObject)
        {
            throw new JsonException("Expected end of JSON object.");
        }
    }

    private static void ReadRequiredPropertyName(ref Utf8JsonReader reader, string propertyName)
    {
        if (!reader.Read() || reader.TokenType is not JsonTokenType.PropertyName)
        {
            throw new JsonException($"Expected property '{propertyName}'.");
        }

        if (!reader.ValueTextEquals(propertyName))
        {
            throw new JsonException($"Expected property '{propertyName}', got '{reader.GetString()}'.");
        }
    }
}
