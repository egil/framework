using System.Text.Json;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxJsonConverterFactoryTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    public void Invalid_items_cannot_become_an_empty_outbox(string items)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Outbox<string>.Create()))!.AsObject();
        json["Items"] = System.Text.Json.Nodes.JsonNode.Parse(items);

        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Outbox<string>>(json.ToJsonString()));

        Assert.Contains("Items", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_sequence_history_is_rejected()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Outbox<string>.Create()))!.AsObject();
        json.Remove("LatestSequenceNumber");

        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Outbox<string>>(json.ToJsonString()));

        Assert.Contains("LatestSequenceNumber", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("first", "second")]
    public void JsonSerializer_round_trips_outbox_with_string_messages(string first, string second)
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var outbox = Outbox<string>.Create();
        outbox = outbox.Add(first, now).Add(second, now);

        var json = JsonSerializer.Serialize(outbox);
        var roundTripped = JsonSerializer.Deserialize<Outbox<string>>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(outbox, roundTripped);
        Assert.Equal(outbox.Revision, roundTripped.Revision);
        Assert.Equal(first, roundTripped[0]);
        Assert.Equal(second, roundTripped[1]);
    }

    [Fact]
    public void Persisted_outbox_does_not_contain_sender_identity()
    {
        var outbox = Outbox<string>.Create()
            .Add("message", new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(outbox);

        Assert.DoesNotContain("Sender", json, StringComparison.Ordinal);
        Assert.DoesNotContain("test/sender", json, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonSerializer_round_trips_outbox_with_complex_messages()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var outbox = Outbox<ComplexMessage>.Create();
        outbox = outbox.Add(new ComplexMessage(
            "order-17",
            42,
            new NestedMessage("north", true)),
            now);

        var json = JsonSerializer.Serialize(outbox);
        var roundTripped = JsonSerializer.Deserialize<Outbox<ComplexMessage>>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(outbox, roundTripped);
        Assert.Equal(outbox.Revision, roundTripped.Revision);
        Assert.Equal("order-17", roundTripped[0].OrderId);
        Assert.Equal(42, roundTripped[0].Quantity);
        Assert.Equal("north", roundTripped[0].Route.Name);
        Assert.True(roundTripped[0].Route.IsExpress);
    }

    [Fact]
    public void JsonSerializer_reads_sender_free_empty_outbox()
    {
        var json = """
            {
              "Revision": "019931d0-0000-7000-8000-000000000001",
              "LatestSequenceNumber": 0,
              "Epoch": null,
              "Items": []
            }
            """;

        var outbox = JsonSerializer.Deserialize<Outbox<string>>(json);
        Assert.NotNull(outbox);
        Assert.Empty(outbox);
    }

    [Fact]
    public void JsonSerializer_throws_json_exception_for_missing_items()
    {
        var json = """
            {
              "Revision": "019931d0-0000-7000-8000-000000000001",
              "LatestSequenceNumber": 0,
              "Epoch": null
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Outbox<string>>(json));
        Assert.Equal("Missing Items.", exception.Message);
    }

    [Fact]
    public void JsonSerializer_throws_json_exception_for_missing_item()
    {
        var json = """
            {
              "LatestSequenceNumber": 1,
              "Epoch": "2026-05-23T12:30:00+00:00",
              "Items": [ null ]
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Outbox<string>>(json));
        Assert.Equal("Missing Item.", exception.Message);
    }

    [Fact]
    public void JsonSerializer_throws_json_exception_for_missing_item_token()
    {
        var json = """
            {
              "LatestSequenceNumber": 1,
              "Epoch": "2026-05-23T12:30:00+00:00",
              "Items": [
                {
                  "Id": null,
                  "Message": "order-17"
                }
              ]
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Outbox<string>>(json));
        Assert.Equal("Missing Id.", exception.Message);
    }

    [Theory]
    [InlineData("SequenceNumber")]
    [InlineData("Timestamp")]
    [InlineData("Epoch")]
    public void Missing_stored_identity_fields_are_rejected(string field)
    {
        var outbox = Outbox<string>.Create().Add("message", DateTimeOffset.UnixEpoch);
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(outbox))!;
        json["Items"]![0]!["Id"]!.AsObject().Remove(field);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Outbox<string>>(json.ToJsonString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_or_empty_revision_cannot_prove_snapshot_identity(bool includeEmptyRevision)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            JsonSerializer.Serialize(Outbox<string>.Create()))!.AsObject();
        json.Remove("Revision");
        if (includeEmptyRevision)
        {
            json["Revision"] = Guid.Empty;
        }

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Outbox<string>>(json.ToJsonString()));

        Assert.Equal("Missing or empty Revision.", exception.Message);
    }

    private sealed record ComplexMessage(string OrderId, int Quantity, NestedMessage Route);

    private sealed record NestedMessage(string Name, bool IsExpress);
}
