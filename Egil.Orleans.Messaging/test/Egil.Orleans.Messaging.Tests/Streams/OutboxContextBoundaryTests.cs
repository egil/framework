using System.Collections.Immutable;
using Egil.Orleans.Messaging.Tests.Outboxes;
using Egil.Orleans.Testing;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class OutboxContextBoundaryTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task Detaching_prevents_a_called_grains_distinct_event_from_being_deduplicated(bool detachIdentity, int expectedEffects)
    {
        var key = Guid.NewGuid();
        var calledGrain = fixture.GrainFactory.GetGrain<IContextBoundarySource>(Guid.NewGuid());
        var receiver = fixture.GrainFactory.GetGrain<IStreamRetryReceiver>(key);
        await receiver.ReadAsync();
        var stream = fixture.Cluster.Client.GetStreamProvider(OutboxProcessorTestProviderNames.Events)
            .GetStream<StreamRetryEvent>(StreamManager.CreateStreamId("stream-retry", receiver.GetGrainId()));
        var token = new OutboxSequenceToken(1, GrainId.Create("sender", "context"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        using (RequestContext.AttachOutboxToken(token))
        {
            await stream.OnNextAsync(new HighStreamRetryEvent(key));
            Assert.Equal(token, await calledGrain.PublishUnrelatedAsync(key, detachIdentity));
            Assert.Equal(token, RequestContext.GetOutboxToken());
        }

        await fixture.WaitForAssertionAsync(receiver, async () =>
            Assert.Equal(2, (await receiver.ReadAsync()).Attempts), ct: TestContext.Current.CancellationToken);
        Assert.Equal(expectedEffects, (await receiver.ReadAsync()).Effects);
        await receiver.DeactivateAsync();
        Assert.Equal(expectedEffects, (await receiver.ReadAsync()).Effects);
    }

    [Fact]
    public async Task A_grain_reads_the_attached_identity_without_a_token_parameter()
    {
        RequestContext.Remove("egil.orleans.messaging.outbox");
        var receiver = fixture.GrainFactory.GetGrain<IContextBoundaryReceiver>(Guid.NewGuid());
        var token = new OutboxSequenceToken(1, GrainId.Create("sender", "context"), DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

        using (RequestContext.AttachOutboxToken(token))
        {
            var received = await receiver.ReadContextTokenAsync();
            Assert.Equal(token, received);
            Assert.Equal(token.TraceParent, received!.TraceParent);
        }

        Assert.Null(await receiver.ReadContextTokenAsync());
    }

    [Fact]
    public async Task Generic_keyed_and_grain_postmen_do_not_establish_ambient_outbox_identity()
    {
        var key = Guid.NewGuid();
        var source = fixture.GrainFactory.GetGrain<IContextBoundarySource>(key);
        var receiver = fixture.GrainFactory.GetGrain<IContextBoundaryReceiver>(key);
        await receiver.ReadAsync();

        Assert.Equal(0, await source.PublishAsync(key));

        await fixture.WaitForAssertionAsync(receiver, async () =>
            Assert.Equal(5, (await receiver.ReadAsync()).Length), ct: TestContext.Current.CancellationToken);
        var observations = await receiver.ReadAsync();
        Assert.All(observations, observation => Assert.False(observation.AmbientIdentity));
        Assert.NotNull(Assert.Single(observations, observation => observation.Path == "rpc").Token);
        Assert.Null(Assert.Single(observations, observation => observation.Path == "raw-delivery").Token);
        Assert.Null(Assert.Single(observations, observation => observation.Path == "keyed").Token);
    }
}

public interface IContextBoundarySource : IGrainWithGuidKey
{
    Task<int> PublishAsync(Guid target);
    Task<OutboxSequenceToken?> PublishUnrelatedAsync(Guid target, bool detachIdentity);
}

public sealed class ContextBoundarySource : Grain, IContextBoundarySource, IOutboxGrain
{
    public async Task<OutboxSequenceToken?> PublishUnrelatedAsync(Guid target, bool detachIdentity)
    {
        var identity = detachIdentity ? RequestContext.DetachOutboxToken() : RequestContext.GetOutboxToken();
        var receiver = GrainFactory.GetGrain<IStreamRetryReceiver>(target);
        var stream = this.GetStreamProvider(OutboxProcessorTestProviderNames.Events)
            .GetStream<StreamRetryEvent>(StreamManager.CreateStreamId("stream-retry", receiver.GetGrainId()));
        await stream.OnNextAsync(new LowStreamRetryEvent(target));
        return identity;
    }

    public async Task<int> PublishAsync(Guid target)
    {
        Outbox<IPayloadEvent> outbox = [new LocalPayload(target, "local"), new GrainPayload(target, "rpc"),
            new SecondStreamPayload(target, "keyed"), new StreamPayload(target, "raw")];
        var processor = this.RegisterOutboxProcessor(() => outbox, options =>
            options.AcknowledgePosted = items => outbox = outbox.RemoveRange(items))
            .AddPostman<LocalPayload>(message => GrainFactory.GetGrain<IContextBoundaryReceiver>(message.Target).RecordAsync("generic", null))
            .AddPostman<GrainPayload>((message, token, grains) =>
                grains.GetGrain<IContextBoundaryReceiver>(message.Target).RecordAsync("rpc", token))
            .AddPostman<SecondStreamPayload>("context-free")
            .AddPostman<StreamPayload>(async ValueTask (message, _) =>
            {
                var receiver = GrainFactory.GetGrain<IContextBoundaryReceiver>(message.Target);
                await receiver.RecordAsync("raw-callback", null);
                await this.GetStreamProvider(OutboxProcessorTestProviderNames.Events).GetStream<StreamPayload>(
                    StreamManager.CreateStreamId("context-boundary", receiver.GetGrainId())).OnNextAsync(message);
            });
        await processor.PostAsync();
        return outbox.Count;
    }
}

public sealed class ContextBoundaryPostman(IGrainFactory grains) : IPostman<SecondStreamPayload>
{
    public ValueTask PostAsync(SecondStreamPayload message, CancellationToken cancellationToken) =>
        new(grains.GetGrain<IContextBoundaryReceiver>(message.Target).RecordAsync("keyed", null));
}

[GenerateSerializer]
public sealed record ContextObservation([property: Id(0)] string Path, [property: Id(1)] bool AmbientIdentity,
    [property: Id(2)] OutboxSequenceToken? Token);

public interface IContextBoundaryReceiver : IGrainWithGuidKey
{
    Task<OutboxSequenceToken?> ReadContextTokenAsync();
    Task RecordAsync(string path, OutboxSequenceToken? token);
    Task<ImmutableArray<ContextObservation>> ReadAsync();
}

public sealed class ContextBoundaryReceiver : Grain, IContextBoundaryReceiver
{
    public async Task<OutboxSequenceToken?> ReadContextTokenAsync()
    {
        await Task.Yield();
        return RequestContext.GetOutboxToken();
    }

    private readonly StreamManager streams;
    private ImmutableArray<ContextObservation> observations = [];
    public ContextBoundaryReceiver()
    {
        streams = this.RegisterStreamManager().ConfigureExplicitSubscription<StreamPayload>(OutboxProcessorTestProviderNames.Events,
            "context-boundary", (_, cursor) => new ValueTask(RecordAsync("raw-delivery", cursor.OutboxToken)));
    }
    public override Task OnActivateAsync(CancellationToken cancellationToken) => streams.EnsureExplicitSubscriptionsAsync(cancellationToken);
    public Task RecordAsync(string path, OutboxSequenceToken? token)
    {
        observations = observations.Add(new(path, RequestContext.Keys.Contains("egil.orleans.messaging.outbox"), token));
        return Task.CompletedTask;
    }
    public Task<ImmutableArray<ContextObservation>> ReadAsync() => Task.FromResult(observations);
}
