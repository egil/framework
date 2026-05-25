using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans.Providers.Streams.Common;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Egil.Orleans.Messaging;

/// <summary>
/// STJ converter for <see cref="StreamCursor"/>. Serializes and
/// deserializes the cursor's <see cref="StreamCursor.StreamNamespace"/> and
/// sequence token.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StreamCursor"/> wraps a stream namespace and an optional
/// <c>StreamSequenceToken</c>. The token is polymorphic and this converter
/// supports Orleans built-in sequence token types only:
/// <see cref="EnrichedEventHubSequenceToken"/>,
/// <c>EventHubSequenceTokenV2</c>,
/// <c>EventHubSequenceToken</c>, <c>EventSequenceTokenV2</c>, and
/// <c>EventSequenceToken</c>.
/// </para>
/// <para>
/// Registered on <see cref="StreamCursor"/> via <c>[JsonConverter]</c>.
/// STJ discovers the attribute automatically — no user-side
/// <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// </remarks>
internal sealed class StreamCursorJsonConverter : JsonConverter<StreamCursor>
{
    private const string FeatureRequestUrl = "https://github.com/egil/framework/issues";

    private const string SupportedKinds =
        "EventSequenceToken, EventSequenceTokenV2, EventHubSequenceToken, EventHubSequenceTokenV2, EnrichedEventHubSequenceToken";

    /// <inheritdoc/>
    public override StreamCursor? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var model = JsonSerializer.Deserialize<StreamCursorJsonModel>(ref reader, options);
        if (model is null)
        {
            return null;
        }

        return new StreamCursor(model.StreamNamespace, FromJsonModel(model.Token), model.ProviderName);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, StreamCursor value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(
            writer,
            new StreamCursorJsonModel(
                value.StreamNamespace,
                ToJsonModel(value.Token),
                value.ProviderName),
            options);
    }

    private static StreamSequenceToken? FromJsonModel(StreamSequenceTokenJsonModel? model)
    {
        if (model is null)
        {
            return null;
        }

        return model.Kind switch
        {
            StreamSequenceTokenKinds.EventSequenceToken => new EventSequenceToken(model.SequenceNumber, model.EventIndex),
            StreamSequenceTokenKinds.EventSequenceTokenV2 => new EventSequenceTokenV2(model.SequenceNumber, model.EventIndex),
            StreamSequenceTokenKinds.EventHubSequenceToken => new EventHubSequenceToken(
                model.EventHubOffset ?? throw new JsonException("Missing EventHubOffset."),
                model.SequenceNumber,
                model.EventIndex),
            StreamSequenceTokenKinds.EventHubSequenceTokenV2 => new EventHubSequenceTokenV2(
                model.EventHubOffset ?? throw new JsonException("Missing EventHubOffset."),
                model.SequenceNumber,
                model.EventIndex),
            StreamSequenceTokenKinds.EnrichedEventHubSequenceToken => new EnrichedEventHubSequenceToken(
                model.EventHubOffset ?? throw new JsonException("Missing EventHubOffset."),
                model.SequenceNumber,
                model.EventIndex,
                model.EnqueuedTime ?? throw new JsonException("Missing EnqueuedTime."),
                model.ProviderName ?? throw new JsonException("Missing ProviderName."),
                model.TraceParent),
            _ => throw CreateUnsupportedTokenException(model.Kind ?? "<null>")
        };
    }

    private static StreamSequenceTokenJsonModel? ToJsonModel(StreamSequenceToken? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            EnrichedEventHubSequenceToken token => new StreamSequenceTokenJsonModel(
                Kind: StreamSequenceTokenKinds.EnrichedEventHubSequenceToken,
                SequenceNumber: token.SequenceNumber,
                EventIndex: token.EventIndex,
                EventHubOffset: token.EventHubOffset,
                EnqueuedTime: token.EnqueuedTime.UtcDateTime,
                ProviderName: token.ProviderName,
                TraceParent: token.TraceParent),
            EventHubSequenceTokenV2 token => new StreamSequenceTokenJsonModel(
                Kind: StreamSequenceTokenKinds.EventHubSequenceTokenV2,
                SequenceNumber: token.SequenceNumber,
                EventIndex: token.EventIndex,
                EventHubOffset: token.EventHubOffset),
            EventHubSequenceToken token => new StreamSequenceTokenJsonModel(
                Kind: StreamSequenceTokenKinds.EventHubSequenceToken,
                SequenceNumber: token.SequenceNumber,
                EventIndex: token.EventIndex,
                EventHubOffset: token.EventHubOffset),
            EventSequenceTokenV2 token => new StreamSequenceTokenJsonModel(
                Kind: StreamSequenceTokenKinds.EventSequenceTokenV2,
                SequenceNumber: token.SequenceNumber,
                EventIndex: token.EventIndex),
            EventSequenceToken token => new StreamSequenceTokenJsonModel(
                Kind: StreamSequenceTokenKinds.EventSequenceToken,
                SequenceNumber: token.SequenceNumber,
                EventIndex: token.EventIndex),
            _ => throw CreateUnsupportedTokenException(value.GetType().FullName ?? value.GetType().Name)
        };
    }

    private static NotSupportedException CreateUnsupportedTokenException(string tokenIdentifier) =>
        new(
            $"Unsupported stream sequence token '{tokenIdentifier}'. " +
            $"This converter supports only built-in Orleans token types: {SupportedKinds}. " +
            "Custom token support is intentionally disabled to keep persisted StreamCursor JSON format stable. " +
            $"If you need this feature, please request it at {FeatureRequestUrl}.");

    private sealed record StreamCursorJsonModel(
        string StreamNamespace,
        StreamSequenceTokenJsonModel? Token,
        string? ProviderName = null);

    private sealed record StreamSequenceTokenJsonModel(
        string? Kind = null,
        long SequenceNumber = 0,
        int EventIndex = 0,
        string? EventHubOffset = null,
        DateTime? EnqueuedTime = null,
        string? ProviderName = null,
        string? TraceParent = null);

    private static class StreamSequenceTokenKinds
    {
        public const string EventSequenceToken = "event-sequence";
        public const string EventSequenceTokenV2 = "event-sequence-v2";
        public const string EventHubSequenceToken = "event-hub";
        public const string EventHubSequenceTokenV2 = "event-hub-v2";
        public const string EnrichedEventHubSequenceToken = "enriched-event-hub";
    }
}
