using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxSequenceTokenJsonConverterTests
{
    [Theory]
    [InlineData("SequenceNumber")]
    [InlineData("Timestamp")]
    [InlineData("Epoch")]
    public void Delivery_identity_cannot_be_reconstructed_with_missing_fields(string missingProperty)
    {
        var token = new OutboxSequenceToken(0, GrainId.Create("test/sender", "one"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(token))!.AsObject();
        json.Remove(missingProperty);

        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OutboxSequenceToken>(json.ToJsonString()));

        Assert.Contains(missingProperty, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_sender_cannot_be_used_as_a_delivery_identity()
    {
        var token = new OutboxSequenceToken(1, GrainId.Create("test/sender", "one"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(token))!.AsObject();
        json["Sender"] = null;

        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OutboxSequenceToken>(json.ToJsonString()));

        Assert.Contains("Sender", error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string> InvalidKnownPropertyJson => new()
    {
        { """{"SequenceNumber":"7"}""" },
        { """{"SequenceNumber":1.5}""" },
        { """{"Timestamp":7}""" },
        { """{"Timestamp":"not-a-date"}""" },
        { """{"Epoch":7}""" },
        { """{"Epoch":"not-a-date"}""" }
    };

    public static TheoryData<GrainId> GrainIdCases => new()
    {
        GrainId.Create("test/type", "plain-string"),
        GrainId.Create(GrainType.Create("test/type"), GrainIdKeyExtensions.CreateIntegerKey(42)),
        GrainId.Create(GrainType.Create("test/type"), GrainIdKeyExtensions.CreateIntegerKey(7, "ext")),
        GrainId.Create(GrainType.Create("test/type"), GrainIdKeyExtensions.CreateGuidKey(Guid.Parse("11111111-2222-3333-4444-555555555555"))),
        GrainId.Create(GrainType.Create("test/type"), GrainIdKeyExtensions.CreateGuidKey(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "ext2"))
    };

    [Fact]
    public void OutboxSequenceToken_is_decorated_with_outbox_sequence_token_json_converter()
    {
        var attribute = typeof(OutboxSequenceToken).GetCustomAttributes(typeof(JsonConverterAttribute), inherit: false)
            .Cast<JsonConverterAttribute>()
            .Single();

        Assert.Equal(typeof(OutboxSequenceTokenJsonConverter), attribute.ConverterType);
    }

    [Fact]
    public void JsonSerializer_round_trips_outbox_sequence_token_without_custom_options()
    {
        var now = new DateTimeOffset(2026, 5, 24, 16, 0, 0, TimeSpan.Zero);
        var sender = GrainId.Create("test/sender", "one");
        var token = new OutboxSequenceToken(7, sender, now, now.AddMinutes(-5));

        var json = JsonSerializer.Serialize(token);
        var roundTripped = JsonSerializer.Deserialize<OutboxSequenceToken>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(token, roundTripped);
    }

    [Theory]
    [MemberData(nameof(GrainIdCases))]
    public void JsonSerializer_round_trips_outbox_sequence_token_for_supported_grain_id_key_shapes(GrainId sender)
    {
        var now = new DateTimeOffset(2026, 5, 24, 16, 0, 0, TimeSpan.Zero);
        var token = new OutboxSequenceToken(9, sender, now, now.AddMinutes(-5));

        var json = JsonSerializer.Serialize(token);
        var roundTripped = JsonSerializer.Deserialize<OutboxSequenceToken>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(token, roundTripped);
    }

    [Fact]
    public void JsonSerializer_throws_for_outbox_sequence_token_payload_missing_sender()
    {
        var json = """
            {
              "SequenceNumber": 7,
              "Timestamp": "2026-05-24T16:00:00+00:00",
              "Epoch": "2026-05-24T15:55:00+00:00"
            }
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OutboxSequenceToken>(json));
    }

    [Theory]
    [MemberData(nameof(InvalidKnownPropertyJson))]
    public void JsonSerializer_reports_invalid_known_property_as_json_error(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OutboxSequenceToken>(json));
    }
}
