using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// STJ converter for <see cref="OutboxSequenceToken"/>. Serializes and
/// deserializes the token's <see cref="OutboxSequenceToken.Sender"/>,
/// <see cref="OutboxSequenceToken.SequenceNumber"/>, and
/// <see cref="OutboxSequenceToken.Epoch"/> properties.
/// </summary>
/// <remarks>
/// <para>
/// Registered on <see cref="OutboxSequenceToken"/> via <c>[JsonConverter]</c>.
/// STJ discovers the attribute automatically — no user-side
/// <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// </remarks>
internal sealed class OutboxSequenceTokenJsonConverter : JsonConverter<OutboxSequenceToken>
{
    /// <inheritdoc/>
    public override OutboxSequenceToken? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var model = JsonSerializer.Deserialize<OutboxSequenceTokenJsonModel>(ref reader, options);
        if (model is null)
        {
            return null;
        }

        if (model.Sender is null)
        {
            throw new JsonException("Missing Sender.");
        }

        return new OutboxSequenceToken(
            model.SequenceNumber,
            GrainId.Create(model.Sender.Type, model.Sender.Key),
            model.Timestamp,
            model.Epoch);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, OutboxSequenceToken value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(
            writer,
            new OutboxSequenceTokenJsonModel(
                value.SequenceNumber,
                new GrainIdJsonModel(value.Sender.Type.ToString()!, value.Sender.Key.ToString()!),
                value.Timestamp,
                value.Epoch),
            options);
    }

    private sealed record OutboxSequenceTokenJsonModel(
        long SequenceNumber,
        GrainIdJsonModel? Sender,
        DateTimeOffset Timestamp,
        DateTimeOffset Epoch);

    private sealed record GrainIdJsonModel(string Type, string Key);
}