using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Streams;

/// <summary>
/// STJ converter for <see cref="StreamCursor"/>. Serializes and
/// deserializes the cursor's <see cref="StreamCursor.StreamNamespace"/> and
/// sequence token.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StreamCursor"/> wraps a stream namespace and an optional
/// <c>StreamSequenceToken</c>. The token is polymorphic and this converter
/// supports provider-neutral Orleans sequence token types only:
/// <c>EventSequenceTokenV2</c> and <c>EventSequenceToken</c>.
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
        "EventSequenceToken, EventSequenceTokenV2";

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
        string? EventHubOffset = null);

    private static class StreamSequenceTokenKinds
    {
        public const string EventSequenceToken = "event-sequence";
        public const string EventSequenceTokenV2 = "event-sequence-v2";
    }
}