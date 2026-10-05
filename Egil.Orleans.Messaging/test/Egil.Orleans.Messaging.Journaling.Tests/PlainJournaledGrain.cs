using Orleans.Journaling;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public interface IPlainJournaledGrain : IGrainWithGuidKey
{
    Task<OrderView> ReadAsync();
    Task ReceiveAsync(OutboxSequenceToken token, string text, bool compact);
    Task DeactivateAsync();
    Task<bool> RejectLateRegistrationAsync();
}

public sealed class PlainJournaledGrain : Grain, IPlainJournaledGrain
{
    private readonly Guid activation = Guid.NewGuid();
    private readonly IDurableStateManager manager;
    private readonly IDurableValue<OrderState> business;
    private readonly IDurableMessageTracker tracker;
    private readonly IDurableOutbox<OrderEvent> outbox;
    private readonly RecordingJournalStorageProvider storage;

    public PlainJournaledGrain(IDurableStateManager manager, RecordingJournalStorageProvider storage,
        [FromKeyedServices("outbox")] IDurableOutbox<OrderEvent> injectedOutbox,
        [FromKeyedServices("tracker")] IDurableMessageTracker injectedTracker)
    {
        this.manager = manager;
        this.storage = storage;
        business = manager.GetOrAddValue<OrderState>("business");
        tracker = manager.GetOrAddState<IDurableMessageTracker>("tracker");
        outbox = manager.GetOrAddState<IDurableOutbox<OrderEvent>>("outbox");
        if (!ReferenceEquals(injectedOutbox, outbox) || !ReferenceEquals(injectedTracker, tracker)
            || !manager.TryGetState<IDurableOutbox<OrderEvent>>("outbox", out var existing) || !ReferenceEquals(existing, outbox))
            throw new InvalidOperationException("Named messaging components are not canonical.");
    }

    public Task<OrderView> ReadAsync() => Task.FromResult(new OrderView(activation, business.Value ?? new(0), tracker.AsImmutable(), outbox.AsImmutable()));

    public async Task ReceiveAsync(OutboxSequenceToken token, string text, bool compact)
    {
        if (!tracker.TryAcceptMessage(token)) return;
        business.Value = new OrderState((business.Value?.Accepted ?? 0) + 1);
        outbox.Add(new OrderEvent(text));
        storage.For(this.GetGrainId()).CompactNext = compact;
        await manager.WriteStateAsync();
    }

    public Task<bool> RejectLateRegistrationAsync()
    {
        Assert.Same(outbox, manager.GetOrAddState<IDurableOutbox<OrderEvent>>("outbox"));
        Assert.Throws<InvalidOperationException>(() => manager.GetOrAddState<IDurableOutbox<int>>("outbox"));
        Assert.Throws<InvalidOperationException>(() => manager.GetOrAddState<IDurableOutbox<int>>("late"));
        Assert.Throws<InvalidOperationException>(() => manager.GetOrAddState<IDurableMessageTracker>("late-tracker"));
        return Task.FromResult(true);
    }

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}
