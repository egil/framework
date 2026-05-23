using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging;

/// <summary>
/// STJ converter factory that creates closed <see cref="JsonConverter{T}"/>
/// instances for <see cref="Outbox{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered on <see cref="Outbox{T}"/> via <c>[JsonConverter]</c>. STJ
/// discovers the attribute automatically — no user-side
/// <see cref="JsonSerializerOptions"/> configuration needed.
/// </para>
/// <para>
/// The converter serializes only the structural data needed to reconstruct
/// the outbox (sender, epoch, sequence numbers, items). Internal
/// non-restorable service references such as the time provider are excluded.
/// </para>
/// </remarks>
internal sealed class OutboxJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsGenericType
            && typeToConvert.GetGenericTypeDefinition() == typeof(Outbox<>);
    }

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var messageType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OutboxJsonConverter<>).MakeGenericType(messageType);

        return (JsonConverter?)Activator.CreateInstance(converterType);
    }

    private sealed class OutboxJsonConverter<T> : JsonConverter<Outbox<T>>
    {
        public override Outbox<T>? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var model = JsonSerializer.Deserialize<OutboxJsonModel<T>>(ref reader, options);
            if (model is null)
            {
                return null;
            }

            return new Outbox<T>(
                GrainId.Create(model.Sender.Type, model.Sender.Key),
                model.LatestSequenceNumber,
                [.. model.Items.Select(static item => new OutboxMessageEnvelope<T>(
                    new OutboxSequenceToken(
                        item.Token.SequenceNumber,
                        GrainId.Create(item.Token.Sender.Type, item.Token.Sender.Key),
                        item.Token.Timestamp,
                        item.Token.Epoch),
                    item.Message))],
                model.Epoch);
        }

        public override void Write(
            Utf8JsonWriter writer,
            Outbox<T> value,
            JsonSerializerOptions options)
        {
            var model = new OutboxJsonModel<T>(
                ToJsonModel(value.Sender),
                value.LatestSequenceNumber,
                value.Epoch,
                [.. value.Select(static item => new OutboxMessageEnvelopeJsonModel<T>(
                    new OutboxSequenceTokenJsonModel(
                        item.Token.SequenceNumber,
                        ToJsonModel(item.Token.Sender),
                        item.Token.Timestamp,
                        item.Token.Epoch),
                    item.Message))]);

            JsonSerializer.Serialize(writer, model, options);
        }
    }

    private sealed record OutboxJsonModel<T>(
        GrainIdJsonModel Sender,
        long LatestSequenceNumber,
        DateTimeOffset? Epoch,
        ImmutableArray<OutboxMessageEnvelopeJsonModel<T>> Items);

    private sealed record OutboxMessageEnvelopeJsonModel<T>(
        OutboxSequenceTokenJsonModel Token,
        T Message);

    private sealed record OutboxSequenceTokenJsonModel(
        long SequenceNumber,
        GrainIdJsonModel Sender,
        DateTimeOffset Timestamp,
        DateTimeOffset Epoch);

    private sealed record GrainIdJsonModel(string Type, string Key);

    private static GrainIdJsonModel ToJsonModel(GrainId grainId) =>
        new(grainId.Type.ToString()!, grainId.Key.ToString()!);
}
