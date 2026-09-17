using System.Text.Json;
using Egil.Orleans.Messaging.Streams.EventHubs.Tests.EventHubs;

namespace Egil.Orleans.Messaging.Tests.Streams.EventHubs;

public sealed class EnrichedEventHubAdapterExtensionsTests
{
    [Fact]
    public void UseEnrichedDataAdapter_throws_for_null_configurator()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            EnrichedEventHubAdapterExtensions.UseEnrichedDataAdapter(null!));

        Assert.Equal("configurator", ex.ParamName);
    }

    [Fact]
    public void UseEnrichedDataAdapter_registers_configuration_callback()
    {
        var configurator = new FakeEventHubStreamConfigurator("provider-a");

        configurator.UseEnrichedDataAdapter();

        Assert.Equal(1, configurator.ConfigureCalls);
        Assert.NotNull(configurator.LastConfigureAction);
    }

    [Fact]
    public void UseEnrichedDataAdapter_registers_stream_sequence_token_converters()
    {
        var configurator = new FakeEventHubStreamConfigurator("provider-a");
        var token = new EnrichedEventHubSequenceToken(
            "12345",
            42,
            2,
            new DateTimeOffset(2026, 5, 26, 10, 15, 0, TimeSpan.Zero),
            "provider-a",
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

        configurator.UseEnrichedDataAdapter();

        var json = JsonSerializer.Serialize(new StreamCursor("orders", token));
        var restored = JsonSerializer.Deserialize<StreamCursor>(json)!.Token;
        var enriched = Assert.IsType<EnrichedEventHubSequenceToken>(restored);
        Assert.Equal(token.EventHubOffset, enriched.EventHubOffset);
        Assert.Equal(token.EnqueuedTime, enriched.EnqueuedTime);
        Assert.Equal(token.ProviderName, enriched.ProviderName);
        Assert.Equal(token.TraceParent, enriched.TraceParent);
    }
}