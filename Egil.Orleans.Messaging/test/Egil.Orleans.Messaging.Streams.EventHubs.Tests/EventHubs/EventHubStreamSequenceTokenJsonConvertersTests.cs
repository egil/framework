using System.Text.Json;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Streams.EventHubs.Tests.EventHubs;

public sealed class EventHubStreamSequenceTokenJsonConvertersTests
{
    public static TheoryData<StreamSequenceToken> EventHubTokens => new()
    {
        new EventHubSequenceToken("12345", 42, 2),
        new EventHubSequenceTokenV2("12345", 42, 2),
        CreateEnrichedToken(),
    };

    [Theory]
    [MemberData(nameof(EventHubTokens))]
    public void Register_registers_every_event_hub_token_type(StreamSequenceToken token)
    {
        EventHubStreamSequenceTokenJsonConverters.Register();

        AssertRoundTrips(token);
    }

    [Fact]
    public void Register_is_idempotent_across_repeated_calls()
    {
        EventHubStreamSequenceTokenJsonConverters.Register();

        EventHubStreamSequenceTokenJsonConverters.Register();

        AssertRoundTrips(CreateEnrichedToken());
    }

    [Fact]
    public void Register_before_UseEnrichedDataAdapter_does_not_throw()
    {
        EventHubStreamSequenceTokenJsonConverters.Register();
        var configurator = new FakeEventHubStreamConfigurator("provider-a");

        configurator.UseEnrichedDataAdapter();

        AssertRoundTrips(CreateEnrichedToken());
    }

    [Fact]
    public void AddEventHubStreamSequenceTokenJsonConverters_registers_the_converters()
    {
        var services = new ServiceCollection();

        var returned = services.AddEventHubStreamSequenceTokenJsonConverters();

        Assert.Same(services, returned);
        AssertRoundTrips(CreateEnrichedToken());
    }

    private static EnrichedEventHubSequenceToken CreateEnrichedToken() =>
        new(
            "12345",
            42,
            2,
            new DateTimeOffset(2026, 5, 26, 10, 15, 0, TimeSpan.Zero),
            "event-hubs",
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

    // Orleans' EventSequenceToken.Equals compares only SequenceNumber and EventIndex,
    // so Assert.Equal on two tokens passes even when the round trip downcast an
    // enriched token to a bare one — the exact failure this registry exists to prevent.
    // Assert the concrete type and every carried field instead.
    private static void AssertRoundTrips(StreamSequenceToken expected)
    {
        var json = JsonSerializer.Serialize(new StreamCursor("orders", expected));
        var actual = JsonSerializer.Deserialize<StreamCursor>(json)!.Token;

        Assert.NotNull(actual);
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.SequenceNumber, actual.SequenceNumber);
        Assert.Equal(expected.EventIndex, actual.EventIndex);
        Assert.Equal(EventHubOffsetOf(expected), EventHubOffsetOf(actual));

        if (expected is EnrichedEventHubSequenceToken enrichedExpected)
        {
            var enrichedActual = Assert.IsType<EnrichedEventHubSequenceToken>(actual);
            Assert.Equal(enrichedExpected.EnqueuedTime, enrichedActual.EnqueuedTime);
            Assert.Equal(enrichedExpected.ProviderName, enrichedActual.ProviderName);
            Assert.Equal(enrichedExpected.TraceParent, enrichedActual.TraceParent);
        }
    }

    private static string EventHubOffsetOf(StreamSequenceToken token) => token switch
    {
        EventHubSequenceTokenV2 v2 => v2.EventHubOffset,
        EventHubSequenceToken v1 => v1.EventHubOffset,
        _ => throw new InvalidOperationException($"Unexpected token type '{token.GetType()}'."),
    };
}
