using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Egil.Orleans.Testing;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class PayloadPostmanTests(MessagingTestClusterFixture fixture)
    : IClassFixture<MessagingTestClusterFixture>
{
    [Fact]
    public void Polymorphic_payloads_round_trip_with_stored_identity()
    {
        var target = Guid.NewGuid();
        var now = fixture.TimeProvider.GetUtcNow();
        var outbox = Outbox<IPayloadEvent>.Create()
            .Add(new LocalPayload(target, "local"), now)
            .Add(new StreamPayload(target, "stream"), now);

        var json = JsonSerializer.Serialize(outbox);
        var loaded = JsonSerializer.Deserialize<Outbox<IPayloadEvent>>(json);

        Assert.NotNull(loaded);
        Assert.Equal(outbox[0].Id, loaded[0].Id);
        Assert.Equal(outbox[0].Message, Assert.IsType<LocalPayload>(loaded[0].Message));
        Assert.Equal(outbox[1].Message, Assert.IsType<StreamPayload>(loaded[1].Message));
        Assert.DoesNotContain("Sender", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Constructor_registered_postmen_dispatch_polymorphic_payloads_and_acknowledge_original_items()
    {
        var key = Guid.NewGuid();
        var source = fixture.GrainFactory.GetGrain<IPayloadSourceGrain>(key);
        var sink = fixture.GrainFactory.GetGrain<IPayloadSinkGrain>(key);
        await sink.EnsureActiveAsync();

        await source.PublishAsync(key, failAcknowledgment: false);

        await fixture.WaitForAssertionAsync(sink, async () =>
        {
            var deliveries = await sink.GetDeliveriesAsync();
            Assert.Equal(["grain", "local", "stream"], deliveries.Select(item => item.Value).Order().ToArray());
            Assert.All(deliveries, item => Assert.Equal(source.GetGrainId(), item.Token.Sender));
            Assert.All(deliveries, item => Assert.Equal(fixture.TimeProvider.GetUtcNow(), item.Token.Timestamp));
            Assert.Equal([1L, 2L, 3L], deliveries.Select(item => item.Token.SequenceNumber).Order().ToArray());
        }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await source.PendingCountAsync());
    }

    [Fact]
    public async Task Reactivation_retries_with_the_same_delivery_tokens_after_acknowledgment_failure()
    {
        var key = Guid.NewGuid();
        var source = fixture.GrainFactory.GetGrain<IPayloadSourceGrain>(key);
        var sink = fixture.GrainFactory.GetGrain<IPayloadSinkGrain>(key);
        await sink.EnsureActiveAsync();
        await source.PublishAsync(key, failAcknowledgment: true);
        await fixture.WaitForAssertionAsync(sink, async () =>
            Assert.Equal(3, (await sink.GetDeliveriesAsync()).Length), ct: TestContext.Current.CancellationToken);
        var first = await sink.GetDeliveriesAsync();

        await source.RetryAsync();

        await fixture.WaitForAssertionAsync(sink, async () =>
        {
            var deliveries = await sink.GetDeliveriesAsync();
            Assert.Equal(6, deliveries.Length);
            Assert.All(first, item => Assert.Equal(2, deliveries.Count(delivery => delivery == item)));
        }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await source.PendingCountAsync());
    }
}

[JsonPolymorphic]
[JsonDerivedType(typeof(LocalPayload), "local")]
[JsonDerivedType(typeof(StreamPayload), "stream")]
[JsonDerivedType(typeof(GrainPayload), "grain")]
public interface IPayloadEvent
{
    Guid Target { get; }
    string Value { get; }
}

[GenerateSerializer]
public sealed record LocalPayload([property: Id(0)] Guid Target, [property: Id(1)] string Value) : IPayloadEvent;

[GenerateSerializer]
public sealed record StreamPayload([property: Id(0)] Guid Target, [property: Id(1)] string Value) : IPayloadEvent;

[GenerateSerializer]
public sealed record GrainPayload([property: Id(0)] Guid Target, [property: Id(1)] string Value) : IPayloadEvent;

[GenerateSerializer]
public sealed record PayloadDelivery([property: Id(0)] string Value, [property: Id(1)] OutboxSequenceToken Token);

[GenerateSerializer]
public sealed record PayloadSourceState
{
    [Id(0)] public Outbox<IPayloadEvent> Outbox { get; init; } = Outbox<IPayloadEvent>.Create();
}

public interface IPayloadSourceGrain : IGrainWithGuidKey
{
    Task PublishAsync(Guid target, bool failAcknowledgment);
    Task RetryAsync();
    Task<int> PendingCountAsync();
}

public sealed class PayloadSourceGrain : Grain, IPayloadSourceGrain, IOutboxGrain
{
    private readonly IStateManager<PayloadSourceState> manager;
    private readonly OutboxProcessor<IPayloadEvent> processor;
    private readonly ManualTimeProvider time;
    private bool failAcknowledgment;

    public PayloadSourceGrain(
        [PersistentState("payload", "Payload")] IPersistentState<PayloadSourceState> storage,
        ManualTimeProvider time)
    {
        this.time = time;
        manager = this.RegisterStateManager("Payload", storage);
        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<IPayloadEvent>
        {
            PendingItems = () => manager.State.Outbox.ToImmutableArray(),
            AcknowledgePostedAsync = AcknowledgeAsync,
            RetryDelay = TimeSpan.FromMinutes(10)
        })
        .AddPostman<LocalPayload>(async (message, token) => await DeliverLocalAsync(message, token))
        .AddStreamPostman<StreamPayload, PayloadDelivery>(OutboxProcessorTestProviderNames.Events,
            message => StreamManager.CreateStreamId("payload-postmen", GrainFactory.GetGrain<IPayloadSinkGrain>(message.Target).GetGrainId()),
            static (message, token) => new(message.Value, token))
        .AddGrainPostman<GrainPayload, IPayloadSinkGrain>(
            static (message, grains) => grains.GetGrain<IPayloadSinkGrain>(message.Target),
            static async (grain, message, token) => await grain.ReceiveAsync(new(message.Value, token)))
        .AddPostman<IPayloadEvent>(static async message => await RejectUnexpectedPayloadAsync(message))
        .AddPostman<IPayloadEvent>(static async (message, _, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RejectUnexpectedPayloadAsync(message);
        });
    }

    private static ValueTask RejectUnexpectedPayloadAsync(IPayloadEvent message) =>
        ValueTask.FromException(new InvalidOperationException($"Unexpected payload: {message.Value}"));

    private ValueTask DeliverLocalAsync(LocalPayload message, OutboxSequenceToken token) =>
        new(GrainFactory.GetGrain<IPayloadSinkGrain>(message.Target)
            .ReceiveAsync(new(message.Value, token)));

    public async Task PublishAsync(Guid target, bool failAcknowledgment)
    {
        this.failAcknowledgment = failAcknowledgment;
        var now = time.GetUtcNow();
        await manager.WriteAsync(manager.State with
        {
            Outbox = manager.State.Outbox
                .Add(new LocalPayload(target, "local"), now)
                .Add(new StreamPayload(target, "stream"), now)
                .Add(new GrainPayload(target, "grain"), now)
        });
        try
        {
            await processor.PostAsync();
        }
        catch (InvalidOperationException) when (failAcknowledgment)
        {
            DeactivateOnIdle();
        }
    }

    public Task RetryAsync() => processor.PostAsync().AsTask();

    public Task<int> PendingCountAsync() => Task.FromResult(manager.State.Outbox.Count);

    private async ValueTask AcknowledgeAsync(ImmutableArray<OutboxMessageEnvelope<IPayloadEvent>> items, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (failAcknowledgment)
        {
            throw new InvalidOperationException("The delivery landed but acknowledgment could not persist.");
        }

        await manager.WriteAsync(manager.State with { Outbox = manager.State.Outbox.RemoveRange(items.Select(item => item.Id)) });
    }
}

public interface IPayloadSinkGrain : IGrainWithGuidKey
{
    Task EnsureActiveAsync();
    Task ReceiveAsync(PayloadDelivery delivery);
    Task<ImmutableArray<PayloadDelivery>> GetDeliveriesAsync();
}

public sealed class PayloadSinkGrain : Grain, IPayloadSinkGrain
{
    private readonly StreamManager streams;
    private ImmutableArray<PayloadDelivery> deliveries = [];

    public PayloadSinkGrain()
    {
        streams = this.RegisterStreamManager()
            .ConfigureExplicitSubscription<PayloadDelivery>(OutboxProcessorTestProviderNames.Events,
                "payload-postmen", (delivery, _) => new ValueTask(ReceiveAsync(delivery)));
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken) =>
        streams.EnsureExplicitSubscriptionsAsync(cancellationToken);

    public Task EnsureActiveAsync() => Task.CompletedTask;

    public Task ReceiveAsync(PayloadDelivery delivery)
    {
        deliveries = deliveries.Add(delivery);
        return Task.CompletedTask;
    }

    public Task<ImmutableArray<PayloadDelivery>> GetDeliveriesAsync() => Task.FromResult(deliveries);
}
