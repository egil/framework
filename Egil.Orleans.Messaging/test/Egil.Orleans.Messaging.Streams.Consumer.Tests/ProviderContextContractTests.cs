using Azure.Messaging.EventHubs;
using Egil.Orleans.Messaging.Outboxes;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers;
using Orleans.Providers.Streams.AzureQueue;
using Orleans.Serialization;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Streams.Consumer.Tests;

// Use the providers' real body/cache serializers so a context-only test cannot hide a missing token codec.
public sealed class ProviderContextContractTests
{
    private const string Key = "egil.orleans.messaging.outbox";
    private static readonly OutboxSequenceToken Identity = new(1, GrainId.Create("sender", "one"),
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
    private static readonly ProviderEvent Message = new("domain-event");

    [Fact]
    public void Default_memory_body_round_trip_preserves_typed_identity_alongside_the_domain_event()
    {
        using var services = Services();
        IMemoryMessageBodySerializer serializer = new DefaultMemoryMessageBodySerializer(services.GetRequiredService<Serializer<MemoryMessageBody>>());

        var decoded = serializer.Deserialize(serializer.Serialize(new MemoryMessageBody([Message], Context())));

        Assert.Equal(Message, Assert.IsType<ProviderEvent>(Assert.Single(decoded.Events)));
        Assert.NotNull(decoded.RequestContext);
        AssertIdentity(decoded.RequestContext[Key]);
    }

    [Fact]
    public void Default_azure_queue_v2_round_trip_preserves_typed_identity_alongside_the_domain_event()
    {
        using var services = Services();
        var serializer = services.GetRequiredService<Serializer>();
        var adapter = new AzureQueueDataAdapterV2(serializer);
        var source = StreamId.Create("orders", "one");

        var text = adapter.ToQueueMessage(source, [Message], null, Context());
        var container = RoundTrip(serializer, adapter.FromQueueMessage(text, 42));

        Assert.Equal(source, container.StreamId);
        Assert.Equal(Message, Assert.Single(container.GetEvents<ProviderEvent>()).Item1);
        Assert.True(container.ImportRequestContext());
        AssertIdentity(RequestContext.Get(Key));
        Assert.Equal(Identity, RequestContext.GetOutboxToken());
        Assert.Equal("retained", RequestContext.Get("unrelated"));
    }

    [Fact]
    public void Default_event_hubs_body_and_cache_round_trip_preserves_typed_identity_alongside_the_domain_event()
    {
        using var services = Services();
        var serializer = services.GetRequiredService<Serializer>();
        var adapter = new EventHubDataAdapter(serializer);
        var source = StreamId.Create("orders", "one");
        var outgoing = adapter.ToQueueMessage(source, [Message], null, Context());
#pragma warning disable CS0618 // Factory supplies broker-owned fields without claiming a live broker delivery.
        var received = EventHubsModelFactory.EventData(outgoing.EventBody, outgoing.Properties,
            new Dictionary<string, object>(), partitionKey: adapter.GetPartitionKey(source), sequenceNumber: 42,
            offset: 123, enqueuedTime: DateTimeOffset.UnixEpoch);
#pragma warning restore CS0618
        received.SetStreamNamespaceProperty("orders");
        var cached = adapter.FromQueueMessage(adapter.GetStreamPosition("0", received), received,
            DateTime.UnixEpoch, static size => new ArraySegment<byte>(new byte[size]));

        var container = RoundTrip(serializer, adapter.GetBatchContainer(ref cached));

        Assert.Equal(source, container.StreamId);
        Assert.Equal(Message, Assert.Single(container.GetEvents<ProviderEvent>()).Item1);
        Assert.True(container.ImportRequestContext());
        AssertIdentity(RequestContext.Get(Key));
        Assert.Equal(Identity, RequestContext.GetOutboxToken());
        Assert.Equal("retained", RequestContext.Get("unrelated"));
    }

    private static Dictionary<string, object> Context()
    {
        using var scope = RequestContext.AttachOutboxToken(Identity);
        return new() { [Key] = RequestContext.Get(Key)!, ["unrelated"] = "retained" };
    }
    private static ServiceProvider Services() => new ServiceCollection().AddSerializer(builder =>
    {
        builder.AddAssembly(typeof(AzureQueueDataAdapterV2).Assembly);
        builder.AddAssembly(typeof(EventHubDataAdapter).Assembly);
        builder.AddAssembly(typeof(OutboxSequenceToken).Assembly);
        builder.AddAssembly(typeof(ProviderEvent).Assembly);
    }).BuildServiceProvider();
    private static IBatchContainer RoundTrip(Serializer serializer, IBatchContainer value) =>
        Assert.IsAssignableFrom<IBatchContainer>(serializer.Deserialize<IBatchContainer>(serializer.SerializeToArray(value)));
    private static void AssertIdentity(object? value)
    {
        var token = Assert.IsType<OutboxSequenceToken>(value);
        Assert.Equal(Identity, token);
        Assert.Equal(Identity.TraceParent, token.TraceParent);
    }
}

[GenerateSerializer]
public sealed record ProviderEvent([property: Id(0)] string Name);
