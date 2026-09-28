using System.Collections.Immutable;
using Orleans.Providers.Streams.AzureQueue;
using Orleans.Serialization;
using Orleans.Streams;
using Egil.Orleans.Messaging.Tests.Outboxes;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class AggregateStreamIdentityTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    private const string ContextKey = "egil.orleans.messaging.outbox";

    [Fact]
    public async Task Separate_provider_containers_keep_their_identities_through_real_aggregate_delivery()
    {
        var key = Guid.NewGuid();
        var receiver = fixture.GrainFactory.GetGrain<IStreamRetryReceiver>(key);
        await receiver.ReadAsync();
        var source = StreamManager.CreateStreamId("stream-retry", receiver.GetGrainId());
        using var services = new ServiceCollection().AddSerializer(builder =>
        {
            builder.AddAssembly(typeof(AzureQueueDataAdapterV2).Assembly);
            builder.AddAssembly(typeof(StreamRetryEvent).Assembly);
            builder.AddAssembly(typeof(OutboxSequenceToken).Assembly);
        }).BuildServiceProvider();
        var serializer = services.GetRequiredService<Serializer>();
        var adapter = new AzureQueueDataAdapterV2(serializer);
        var capture = new StreamManagerResumeTests.FakeStream<StreamRetryEvent>(OutboxProcessorTestProviderNames.Events, source);
        var publications = new List<IBatchContainer>();
        var identities = new List<OutboxSequenceToken>();
        await capture.SubscribeAsync(new CaptureObserver(message =>
        {
            var context = RequestContext.Keys.ToDictionary(name => name, name => RequestContext.Get(name)!);
            identities.Add(Assert.IsType<OutboxSequenceToken>(context[ContextKey]));
            var text = adapter.ToQueueMessage(source, [message], null, context);
            var container = adapter.FromQueueMessage(text, publications.Count + 1);
            publications.Add(Assert.IsAssignableFrom<IBatchContainer>(serializer.Deserialize<IBatchContainer>(serializer.SerializeToArray<IBatchContainer>(container))));
            return Task.CompletedTask;
        }));
        var first = new OutboxSequenceToken(2, GrainId.Create("sender", "one"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var second = first with { SequenceNumber = 1 };
        await capture.PublishFromOutboxAsync(new(key), first);
        await capture.PublishFromOutboxAsync(new(key), second);
        Assert.Equal(2, publications.Count);
        Assert.NotEqual(identities[0], identities[1]);

        var aggregateSize = await receiver.DeliverAggregateAsync(publications.ToImmutableArray());

        Assert.Equal(2, aggregateSize);
        var delivered = await receiver.DeliveriesAsync();
        Assert.Equal([first, second], delivered.Select(cursor => cursor.OutboxToken));
        Assert.All(delivered, cursor =>
        {
            Assert.Equal(source, cursor.StreamId);
            Assert.Equal(OutboxProcessorTestProviderNames.Events, cursor.ProviderName);
        });
        Assert.Equal(2, (await receiver.ReadAsync()).Effects);
    }

    private sealed class CaptureObserver(Func<StreamRetryEvent, Task> receive) : IAsyncObserver<StreamRetryEvent>
    {
        public Task OnNextAsync(StreamRetryEvent item, StreamSequenceToken? token = null) => receive(item);
        public Task OnCompletedAsync() => Task.CompletedTask;
        public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
    }
}
