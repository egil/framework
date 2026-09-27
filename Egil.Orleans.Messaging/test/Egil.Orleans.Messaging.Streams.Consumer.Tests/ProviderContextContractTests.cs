using Azure.Messaging.EventHubs;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers;
using Orleans.Providers.Streams.AzureQueue;
using Orleans.Serialization;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Streams.Consumer.Tests;

// This executable deliberately has no Messaging reference. Only a versioned primitive string
// crosses the transport; ordinary Orleans consumers must not need the outbox token's assembly.
public sealed class ProviderContextContractTests
{
    private const string Key = "egil.orleans.messaging.outbox";
    private const string Identity = "v1:{\"SequenceNumber\":1,\"Sender\":{\"Type\":\"sender\",\"Key\":\"one\"},\"Timestamp\":\"1970-01-01T00:00:00+00:00\",\"Epoch\":\"1970-01-01T00:00:00+00:00\"}";

    [Fact]
    public void Default_memory_body_round_trip_preserves_primitive_identity_without_messaging_codecs()
    {
        using var services = Services();
        IMemoryMessageBodySerializer serializer = new DefaultMemoryMessageBodySerializer(services.GetRequiredService<Serializer<MemoryMessageBody>>());

        var decoded = serializer.Deserialize(serializer.Serialize(new MemoryMessageBody(["domain-event"], Context())));

        Assert.Equal("domain-event", Assert.Single(decoded.Events));
        Assert.NotNull(decoded.RequestContext);
        Assert.Equal(Identity, Assert.IsType<string>(decoded.RequestContext[Key]));
        AssertNoMessagingAssembly();
    }

    [Fact]
    public void Default_azure_queue_v2_text_round_trip_preserves_identity_for_an_ordinary_subscriber()
    {
        using var services = Services();
        var serializer = services.GetRequiredService<Serializer>();
        var adapter = new AzureQueueDataAdapterV2(serializer);
        var source = StreamId.Create("orders", "one");

        var text = adapter.ToQueueMessage(source, ["domain-event"], null, Context());
        var container = RoundTrip(serializer, adapter.FromQueueMessage(text, 42));

        Assert.Equal(source, container.StreamId);
        Assert.Equal("domain-event", Assert.Single(container.GetEvents<string>()).Item1);
        Assert.True(container.ImportRequestContext());
        Assert.Equal(Identity, Assert.IsType<string>(RequestContext.Get(Key)));
        Assert.Equal("retained", RequestContext.Get("unrelated"));
        AssertNoMessagingAssembly();
    }

    [Fact]
    public void Default_event_hubs_body_and_cache_round_trip_preserves_identity_for_an_ordinary_subscriber()
    {
        using var services = Services();
        var serializer = services.GetRequiredService<Serializer>();
        var adapter = new EventHubDataAdapter(serializer);
        var source = StreamId.Create("orders", "one");
        var outgoing = adapter.ToQueueMessage(source, ["domain-event"], null, Context());
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
        Assert.Equal("domain-event", Assert.Single(container.GetEvents<string>()).Item1);
        Assert.True(container.ImportRequestContext());
        Assert.Equal(Identity, Assert.IsType<string>(RequestContext.Get(Key)));
        Assert.Equal("retained", RequestContext.Get("unrelated"));
        AssertNoMessagingAssembly();
    }

    private static Dictionary<string, object> Context() => new() { [Key] = Identity, ["unrelated"] = "retained" };
    private static ServiceProvider Services() => new ServiceCollection().AddSerializer(builder =>
    {
        builder.AddAssembly(typeof(AzureQueueDataAdapterV2).Assembly);
        builder.AddAssembly(typeof(EventHubDataAdapter).Assembly);
    }).BuildServiceProvider();
    private static IBatchContainer RoundTrip(Serializer serializer, IBatchContainer value) =>
        Assert.IsAssignableFrom<IBatchContainer>(serializer.Deserialize<IBatchContainer>(serializer.SerializeToArray(value)));
    private static void AssertNoMessagingAssembly() => Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(),
        assembly => assembly.GetName().Name is "Egil.Orleans.Messaging" or "Egil.Orleans.Messaging.Streams.EventHubs");
}
