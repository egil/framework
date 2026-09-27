using System.Collections.Immutable;
using Egil.Orleans.Messaging.Tests.Outboxes;
using Egil.Orleans.Testing;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class OutboxContextBoundaryTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
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
}

public sealed class ContextBoundarySource : Grain, IContextBoundarySource, IOutboxGrain
{
    public async Task<int> PublishAsync(Guid target)
    {
        Outbox<IPayloadEvent> outbox = [new LocalPayload(target, "local"), new GrainPayload(target, "rpc"),
            new SecondStreamPayload(target, "keyed"), new StreamPayload(target, "raw")];
        var processor = this.RegisterOutboxProcessor(() => outbox, options =>
            options.AcknowledgePosted = items => outbox = outbox.RemoveRange(items))
            .AddPostman<LocalPayload>(message => GrainFactory.GetGrain<IContextBoundaryReceiver>(message.Target).RecordAsync("generic", null))
            .AddGrainPostman<GrainPayload, IContextBoundaryReceiver>(
                (message, grains) => grains.GetGrain<IContextBoundaryReceiver>(message.Target),
                (receiver, _, token) => receiver.RecordAsync("rpc", token))
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
    Task RecordAsync(string path, OutboxSequenceToken? token);
    Task<ImmutableArray<ContextObservation>> ReadAsync();
}

public sealed class ContextBoundaryReceiver : Grain, IContextBoundaryReceiver
{
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
