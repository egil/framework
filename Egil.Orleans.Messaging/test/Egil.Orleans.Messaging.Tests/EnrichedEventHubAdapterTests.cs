using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers.Streams.Common;
using Orleans.Serialization;

namespace Egil.Orleans.Messaging.Tests;

public sealed class EnrichedEventHubAdapterTests
{
    [Fact]
    public void ToQueueMessage_stamps_traceparent_when_activity_is_active()
    {
        var adapter = CreateAdapter();
        using var activity = new Activity("publish").Start();

        var message = adapter.ToQueueMessage(
            StreamId.Create("orders", "one"),
            ["event-1"],
            null!,
            []);

        var value = Assert.Contains("traceparent", message.Properties);
        Assert.Equal(activity.Id, Assert.IsType<string>(value));
    }

    [Fact]
    public void ToQueueMessage_does_not_stamp_traceparent_when_no_activity_is_active()
    {
        var adapter = CreateAdapter();

        var message = adapter.ToQueueMessage(
            StreamId.Create("orders", "one"),
            ["event-1"],
            null!,
            []);

        Assert.DoesNotContain("traceparent", message.Properties);
    }

    [Fact]
    public void GetSequenceToken_returns_enriched_event_hub_sequence_token()
    {
        var adapter = CreateAdapter();
        var cachedMessage = new CachedMessage
        {
            SequenceNumber = 10,
            EventIndex = 2,
            EnqueueTimeUtc = new DateTime(2026, 5, 24, 19, 0, 0, DateTimeKind.Utc)
        };

        var token = adapter.GetSequenceToken(ref cachedMessage);

        var enriched = Assert.IsType<EnrichedEventHubSequenceToken>(token);
        Assert.Equal(10, enriched.SequenceNumber);
        Assert.Equal(2, enriched.EventIndex);
        Assert.Equal("provider-a", enriched.StreamProviderName);
        Assert.Null(enriched.TraceParent);
    }

    private static EnrichedEventHubAdapter CreateAdapter()
    {
        var services = new ServiceCollection();
        services.AddSerializer(static _ => { });
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<Serializer>();

        return new EnrichedEventHubAdapter("provider-a", serializer);
    }
}
