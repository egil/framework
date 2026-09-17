using System.Collections.Immutable;

namespace Egil.Orleans.Messaging.Tests.State;

/// <summary>
/// The deferred acknowledgement pattern from the README, driven by a real
/// <see cref="OutboxProcessor{TOutbox}"/> on a test cluster rather than by calling
/// <c>Stage</c> directly.
/// </summary>
public sealed class OutboxDeferredAckProcessorTests(MessagingTestClusterFixture fixture)
    : IClassFixture<MessagingTestClusterFixture>
{
    [Fact]
    public async Task Deferred_acknowledgement_empties_the_visible_outbox_but_not_the_durable_one()
    {
        var grain = fixture.GrainFactory.GetGrain<IDeferredAckOutboxGrain>(Guid.NewGuid());

        await grain.PublishAsync("order-placed");

        var observed = await grain.ObserveAsync();
        Assert.Equal(0, observed.Pending);
        Assert.True(observed.HasUnsavedChanges);

        await grain.DeactivateAsync();

        // Nothing persisted the acknowledgement, so the durable outbox still holds the
        // message and the next activation sees it pending again. At-least-once, not loss.
        var reactivated = await grain.ObserveAsync();
        Assert.Equal(1, reactivated.Pending);
        Assert.False(reactivated.HasUnsavedChanges);
    }

    [Fact]
    public async Task Deferred_acknowledgement_is_persisted_by_the_next_business_write()
    {
        var grain = fixture.GrainFactory.GetGrain<IDeferredAckOutboxGrain>(Guid.NewGuid());
        await grain.PublishAsync("order-placed");
        Assert.True((await grain.ObserveAsync()).HasUnsavedChanges);

        await grain.SaveAsync("shipped");
        await grain.DeactivateAsync();

        var reactivated = await grain.ObserveAsync();
        Assert.Equal(0, reactivated.Pending);
        Assert.Equal("shipped", reactivated.Value);
    }
}

public interface IDeferredAckOutboxGrain : IGrainWithGuidKey
{
    Task PublishAsync(string value);
    Task SaveAsync(string value);
    Task<DeferredAckOutboxObservation> ObserveAsync();
    Task DeactivateAsync();
}

[GenerateSerializer]
public sealed record DeferredAckOutboxObservation(
    [property: Id(0)] int Pending,
    [property: Id(1)] bool HasUnsavedChanges,
    [property: Id(2)] string Value);

[GenerateSerializer]
public sealed record DeferredAckOutboxEvent([property: Id(0)] string Value);

[GenerateSerializer]
public sealed record DeferredAckOutboxState
{
    [Id(0)] public string Value { get; init; } = "none";

    [Id(1)] public Outbox<DeferredAckOutboxEvent> Outbox { get; init; } = [];
}

public sealed class DeferredAckOutboxGrain : Grain, IDeferredAckOutboxGrain, IOutboxGrain
{
    private readonly IStateManager<DeferredAckOutboxState> state;
    private OutboxProcessor<DeferredAckOutboxEvent>? processor;

    // The Payload provider, because Outbox<T> ships a System.Text.Json converter and that
    // provider is the fixture's STJ-configured one. The collector-decorated Default
    // provider serializes with Newtonsoft, which cannot round-trip an immutable Outbox<T>.
    public DeferredAckOutboxGrain([PersistentState("state", "Payload")] IPersistentState<DeferredAckOutboxState> storage)
        => state = this.RegisterStateManager("Payload", storage, static () => new());

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<DeferredAckOutboxEvent>
        {
            OutboxAccessor = () => state.State.Outbox,
            AcknowledgePostedAsync = AcknowledgeAsync
        })
        .AddPostman<DeferredAckOutboxEvent>(static _ => Task.CompletedTask);

        return base.OnActivateAsync(cancellationToken);
    }

    public async Task PublishAsync(string value)
    {
        await state.WriteAsync(state.State with { Outbox = state.State.Outbox.Add(new DeferredAckOutboxEvent(value)) });

        // PostAsync rather than PostInBackgroundAsync so the test needs no waiting: a
        // deferred acknowledgement performs no storage write, so it produces no activity
        // for the collector's WaitForAssertionAsync to wake on.
        await processor!.PostAsync();
    }

    public Task SaveAsync(string value) => state.WriteAsync(state.State with { Value = value });

    public Task<DeferredAckOutboxObservation> ObserveAsync() => Task.FromResult(
        new DeferredAckOutboxObservation(state.State.Outbox.Count, state.HasUnsavedChanges, state.State.Value));

    public Task DeactivateAsync()
    {
        // Deliberately no OnDeactivateAsync save: these tests pin what deferring costs when
        // the grain does not persist on the way out.
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    private ValueTask AcknowledgeAsync(
        ImmutableArray<OutboxMessageEnvelope<DeferredAckOutboxEvent>> items,
        CancellationToken cancellationToken)
    {
        // The README's deferred acknowledgement pattern: no storage write here, the next
        // business write carries the removal.
        state.State = state.State with { Outbox = state.State.Outbox.RemoveRange(items) };
        return ValueTask.CompletedTask;
    }
}
