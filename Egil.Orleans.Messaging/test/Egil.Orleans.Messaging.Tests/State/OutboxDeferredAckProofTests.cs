namespace Egil.Orleans.Messaging.Tests.State;

/// <summary>
/// Why deferred writes exist, stated against the state manager alone: an outbox
/// acknowledgement can be applied to the visible snapshot without costing a storage
/// write, and losing it causes redelivery rather than message loss. These drive
/// <c>Stage</c> directly; <see cref="OutboxDeferredAckProcessorTests"/> drives the same
/// pattern through a real <see cref="OutboxProcessor{TOutbox}"/> on a test cluster.
/// </summary>
public sealed class OutboxDeferredAckProofTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Deferred_acknowledgement_costs_no_write_and_leaves_the_durable_outbox_intact()
    {
        var storage = new RecordingStorage();
        var manager = new DefaultStateManager<ShopState>(storage, static () => new());
        // The shape OutboxProcessorOptions<T>.OutboxAccessor is wired to: it reads through
        // the manager, so it observes staged removals with no change at the call site.
        Outbox<string> Outbox() => manager.State.Outbox;
        await manager.WriteAsync(manager.State with
        {
            LastCommand = "place-order",
            Outbox = Outbox().Add("order-placed", Timestamp)
        }, TestContext.Current.CancellationToken);

        manager.State = manager.State with { Outbox = Outbox().RemoveRange(Outbox().Envelopes) };

        Assert.Empty(Outbox());
        Assert.Equal(1, storage.WriteCount);
        // Nothing left the durable outbox, so an activation that ends here redelivers the
        // message on the next post run. At-least-once is preserved either way.
        Assert.Single(storage.Persisted.Outbox);
    }

    [Fact]
    public async Task Deferred_acknowledgement_rides_the_next_business_write()
    {
        var storage = new RecordingStorage();
        var manager = new DefaultStateManager<ShopState>(storage, static () => new());
        await manager.WriteAsync(manager.State with
        {
            LastCommand = "place-order",
            Outbox = manager.State.Outbox.Add("order-placed", Timestamp)
        }, TestContext.Current.CancellationToken);
        manager.State = manager.State with { Outbox = manager.State.Outbox.RemoveRange(manager.State.Outbox.Envelopes) };

        await manager.WriteAsync(manager.State with { LastCommand = "ship-order" }, TestContext.Current.CancellationToken);

        Assert.Equal(2, storage.WriteCount);
        Assert.Equal("ship-order", storage.Persisted.LastCommand);
        Assert.Empty(storage.Persisted.Outbox);
    }

    private sealed record ShopState
    {
        public string LastCommand { get; init; } = "none";

        public Outbox<string> Outbox { get; init; } = Outbox<string>.Create();
    }

    // Keeps the durable snapshot separate from the facet. Assignment mirrors into the facet
    // by design, so only a value that survived a write proves what storage would return.
    private sealed class RecordingStorage : IPersistentState<ShopState>
    {
        public ShopState State { get; set; } = new();

        public ShopState Persisted { get; private set; } = new();

        public string Etag { get; set; } = "etag-1";

        public bool RecordExists => true;

        public int WriteCount { get; private set; }

        public Task ReadStateAsync()
        {
            State = Persisted;
            return Task.CompletedTask;
        }

        public Task WriteStateAsync()
        {
            WriteCount++;
            Persisted = State;
            return Task.CompletedTask;
        }

        public Task ClearStateAsync() => throw new NotSupportedException();

        public Task ReadStateAsync(CancellationToken cancellationToken) => ReadStateAsync();

        public Task WriteStateAsync(CancellationToken cancellationToken) => WriteStateAsync();

        public Task ClearStateAsync(CancellationToken cancellationToken) => ClearStateAsync();
    }
}
