using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Egil.Orleans.Messaging.Streams;

namespace Egil.Orleans.Messaging.Tracking;

/// <summary>
/// STJ converter for <see cref="MessageTracker"/>. Serializes and
/// deserializes the tracker's internal stream-position and outbox-position
/// dictionaries without exposing private fields.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MessageTracker"/> is a sealed class with private
/// <see cref="System.Collections.Immutable.ImmutableDictionary{TKey, TValue}"/>
/// backing fields. Exposing them via <c>[JsonInclude]</c> would leak
/// internal structure and weaken encapsulation. This converter controls
/// the exact wire format.
/// </para>
/// <para>
/// Registered on <see cref="MessageTracker"/> via <c>[JsonConverter]</c>.
/// STJ discovers the attribute automatically - no user-side
/// <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// </remarks>
internal sealed class MessageTrackerJsonConverter : JsonConverter<MessageTracker>
{
    private static readonly FieldInfo StreamsField = typeof(MessageTracker).GetField(
        "streams",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo OutboxField = typeof(MessageTracker).GetField(
        "outbox",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <inheritdoc/>
    public override MessageTracker? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var model = JsonSerializer.Deserialize<MessageTrackerJsonModel>(ref reader, options);
        if (model is null)
        {
            return null;
        }

        var streams = ImmutableDictionary.CreateBuilder<MessageTracker.StreamSource, MessageTracker.StreamEntry>();
        foreach (var item in model.Streams ?? Array.Empty<StreamEntryJsonModel>())
        {
            var lastPosition = item.LastPosition;
            streams.Add(
                MessageTracker.StreamSource.From(lastPosition),
                new MessageTracker.StreamEntry(lastPosition, item.Received));
        }

        var outbox = ImmutableDictionary.CreateBuilder<GrainId, MessageTracker.OutboxEntry>();
        foreach (var item in model.Outboxes ?? Array.Empty<OutboxEntryJsonModel>())
        {
            outbox.Add(
                ParseGrainId(item.Sender),
                new MessageTracker.OutboxEntry(
                    item.Epoch,
                    item.LastSequenceNumber,
                    item.Received));
        }

        return new MessageTracker(streams.ToImmutable(), outbox.ToImmutable());
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, MessageTracker value, JsonSerializerOptions options)
    {
        var streams = GetStreams(value);
        var outbox = GetOutbox(value);

        var streamModels = new StreamEntryJsonModel[streams.Count];
        var streamIndex = 0;
        foreach (var item in streams)
        {
            streamModels[streamIndex++] = new StreamEntryJsonModel(
                item.Value.LastPosition,
                item.Value.Received);
        }

        var outboxModels = new OutboxEntryJsonModel[outbox.Count];
        var outboxIndex = 0;
        foreach (var item in outbox)
        {
            outboxModels[outboxIndex++] = new OutboxEntryJsonModel(
                ToJsonModel(item.Key),
                item.Value.Epoch,
                item.Value.LastSequenceNumber,
                item.Value.Received);
        }

        JsonSerializer.Serialize(
            writer,
            new MessageTrackerJsonModel(streamModels, outboxModels),
            options);
    }

    private static ImmutableDictionary<MessageTracker.StreamSource, MessageTracker.StreamEntry> GetStreams(MessageTracker value) =>
        (ImmutableDictionary<MessageTracker.StreamSource, MessageTracker.StreamEntry>)StreamsField.GetValue(value)!;

    private static ImmutableDictionary<GrainId, MessageTracker.OutboxEntry> GetOutbox(MessageTracker value) =>
        (ImmutableDictionary<GrainId, MessageTracker.OutboxEntry>)OutboxField.GetValue(value)!;

    private static GrainId ParseGrainId(GrainIdJsonModel value) =>
        GrainId.Create(value.Type, value.Key);

    private static GrainIdJsonModel ToJsonModel(GrainId grainId) =>
        new(grainId.Type.ToString()!, grainId.Key.ToString()!);

    private sealed record MessageTrackerJsonModel(
        StreamEntryJsonModel[]? Streams,
        OutboxEntryJsonModel[]? Outboxes);

    private sealed record StreamEntryJsonModel(
        StreamCursor LastPosition,
        DateTimeOffset Received);

    private sealed record OutboxEntryJsonModel(
        GrainIdJsonModel Sender,
        DateTimeOffset Epoch,
        long LastSequenceNumber,
        DateTimeOffset Received);

    private sealed record GrainIdJsonModel(
        string Type,
        string Key);
}
