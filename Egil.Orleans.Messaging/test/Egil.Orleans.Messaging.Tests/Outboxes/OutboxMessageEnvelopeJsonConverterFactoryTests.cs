using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxMessageEnvelopeJsonConverterFactoryTests
{
    [Fact]
    public void Persisted_envelope_must_distinguish_missing_payload_from_explicit_null()
    {
        const string missingProperty = "Message";
        var envelope = new OutboxMessageEnvelope<string>(new(1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), "payload");
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(envelope))!.AsObject();
        json.Remove(missingProperty);

        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OutboxMessageEnvelope<string>>(json.ToJsonString()));

        Assert.Contains(missingProperty, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_null_payload_round_trips_and_unknown_nested_metadata_is_ignored()
    {
        var id = new OutboxMessageId(1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var envelope = new OutboxMessageEnvelope<string?>(id, null);
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(envelope))!.AsObject();
        json["FutureMetadata"] = System.Text.Json.Nodes.JsonNode.Parse("""{"nested":[1,{"Id":"unrelated"}]}""");

        var loaded = JsonSerializer.Deserialize<OutboxMessageEnvelope<string?>>(json.ToJsonString());

        Assert.Equal(envelope, loaded);
    }

    [Fact]
    public void OutboxMessageEnvelope_is_decorated_with_converter_factory()
    {
        var attribute = typeof(OutboxMessageEnvelope<string>)
            .GetCustomAttributes(typeof(JsonConverterAttribute), inherit: false)
            .Cast<JsonConverterAttribute>()
            .Single();

        Assert.Equal(typeof(OutboxMessageEnvelopeJsonConverterFactory), attribute.ConverterType);
    }

    [Fact]
    public void CanConvert_returns_true_for_outbox_message_envelope_generic_type()
    {
        var factory = new OutboxMessageEnvelopeJsonConverterFactory();

        var canConvert = factory.CanConvert(typeof(OutboxMessageEnvelope<string>));

        Assert.True(canConvert);
    }

    [Fact]
    public void CanConvert_returns_false_for_non_outbox_message_envelope_type()
    {
        var factory = new OutboxMessageEnvelopeJsonConverterFactory();

        var canConvert = factory.CanConvert(typeof(Outbox<string>));

        Assert.False(canConvert);
    }

    [Fact]
    public void JsonSerializer_round_trips_outbox_message_envelope_without_custom_options()
    {
        var now = new DateTimeOffset(2026, 5, 24, 18, 0, 0, TimeSpan.Zero);
        var token = new OutboxMessageId(11, now, now.AddMinutes(-1));
        var envelope = new OutboxMessageEnvelope<string>(token, "hello");

        var json = JsonSerializer.Serialize(envelope);
        var roundTripped = JsonSerializer.Deserialize<OutboxMessageEnvelope<string>>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(envelope, roundTripped);
    }

    [Fact]
    public void JsonSerializer_throws_for_outbox_message_envelope_payload_missing_token()
    {
        var json = """
            {
              "Message": "hello"
            }
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OutboxMessageEnvelope<string>>(json));
    }
}