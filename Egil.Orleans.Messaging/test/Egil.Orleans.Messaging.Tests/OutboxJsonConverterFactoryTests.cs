using System.Text.Json;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests;

public sealed class OutboxJsonConverterFactoryTests
{
    [Theory]
    [InlineData("first", "second")]
    public void JsonSerializer_round_trips_outbox_with_string_messages(string first, string second)
    {
        var sender = GrainId.Create("test/sender", "one");
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(now);
        var outbox = Outbox<string>.Create(sender);
        outbox.RegisterTimeProvider(time);
        outbox = outbox.Add(first).Add(second);

        var json = JsonSerializer.Serialize(outbox);
        var roundTripped = JsonSerializer.Deserialize<Outbox<string>>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(outbox, roundTripped);
        Assert.Equal(first, roundTripped[0].Message);
        Assert.Equal(second, roundTripped[1].Message);
    }

    [Fact]
    public void JsonSerializer_round_trips_outbox_with_complex_messages()
    {
        var sender = GrainId.Create("test/sender", "one");
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(now);
        var outbox = Outbox<ComplexMessage>.Create(sender);
        outbox.RegisterTimeProvider(time);
        outbox = outbox.Add(new ComplexMessage(
            "order-17",
            42,
            new NestedMessage("north", true)));

        var json = JsonSerializer.Serialize(outbox);
        var roundTripped = JsonSerializer.Deserialize<Outbox<ComplexMessage>>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(outbox, roundTripped);
        Assert.Equal("order-17", roundTripped[0].Message.OrderId);
        Assert.Equal(42, roundTripped[0].Message.Quantity);
        Assert.Equal("north", roundTripped[0].Message.Route.Name);
        Assert.True(roundTripped[0].Message.Route.IsExpress);
    }

    [Fact]
    public void JsonSerializer_throws_json_exception_for_missing_sender()
    {
        var json = """
            {
              "LatestSequenceNumber": 0,
              "Epoch": null,
              "Items": []
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Outbox<string>>(json));
        Assert.Equal("Missing Sender.", exception.Message);
    }

    [Fact]
    public void JsonSerializer_throws_json_exception_for_missing_items()
    {
        var json = """
            {
              "Sender": { "Type": "test/sender", "Key": "one" },
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
              "Sender": { "Type": "test/sender", "Key": "one" },
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
              "Sender": { "Type": "test/sender", "Key": "one" },
              "LatestSequenceNumber": 1,
              "Epoch": "2026-05-23T12:30:00+00:00",
              "Items": [
                {
                  "Token": null,
                  "Message": "order-17"
                }
              ]
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Outbox<string>>(json));
        Assert.Equal("Missing Token.", exception.Message);
    }

    [Fact]
    public void JsonSerializer_throws_json_exception_for_missing_item_token_sender()
    {
        var json = """
            {
              "Sender": { "Type": "test/sender", "Key": "one" },
              "LatestSequenceNumber": 1,
              "Epoch": "2026-05-23T12:30:00+00:00",
              "Items": [
                {
                  "Token": {
                    "SequenceNumber": 1,
                    "Sender": null,
                    "Timestamp": "2026-05-23T12:30:00+00:00",
                    "Epoch": "2026-05-23T12:30:00+00:00"
                  },
                  "Message": "order-17"
                }
              ]
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Outbox<string>>(json));
        Assert.Equal("Missing Token.Sender.", exception.Message);
    }

    private sealed record ComplexMessage(string OrderId, int Quantity, NestedMessage Route);

    private sealed record NestedMessage(string Name, bool IsExpress);
}
