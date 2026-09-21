#pragma warning disable ORLEANSEXP005 // This suite explicitly exercises the pinned experimental journaling package.
using Orleans.Journaling;
using Orleans.Providers.Streams.Common;
using TimeProviderExtensions;
using Orleans.TestingHost;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class JournalingPrototypeTests(JournalingPrototypeFixture fixture)
    : IClassFixture<JournalingPrototypeFixture>
{
    [Fact]
    public async Task One_commit_recovers_business_state_tracker_and_outbox_together()
    {
        var grain = fixture.NewGrain();
        var token = Token(1);

        Assert.True(await grain.ReceiveAsync(token, "first", TestContext.Current.CancellationToken));
        var saved = await grain.ReadAsync();
        Assert.Single(fixture.Storage.For(grain.GetGrainId()).Writes);
        await grain.DeactivateAsync();
        var recovered = await grain.ReadAsync();

        Assert.NotEqual(saved.Activation, recovered.Activation);
        Assert.Equal(1, recovered.Business.Accepted);
        Assert.Equal(token, recovered.Tracker.LatestOutbox(token.Sender));
        Assert.Equal(saved.Outbox, recovered.Outbox);
        Assert.Equal("first", Assert.Single(recovered.Outbox).Text);
        Assert.False(await grain.ReceiveAsync(token, "duplicate", TestContext.Current.CancellationToken));
        Assert.Equal(1, (await grain.ReadAsync()).Business.Accepted);
    }

    [Fact]
    public async Task Posted_messages_are_removed_without_rewriting_business_state_or_payloads()
    {
        var grain = fixture.NewGrain();
        await grain.ReceiveAsync(Token(1), "payload-only-in-the-append", TestContext.Current.CancellationToken);
        var storage = fixture.Storage.For(grain.GetGrainId());
        var before = storage.Writes.Count;

        await grain.PostAsync(TestContext.Current.CancellationToken);

        var after = await grain.ReadAsync();
        Assert.Empty(after.Outbox);
        Assert.Single(fixture.Delivered.For(grain.GetGrainId()));
        Assert.Equal(before + 1, storage.Writes.Count);
        var acknowledgement = storage.Writes[^1];
        Assert.DoesNotContain("payload-only-in-the-append", acknowledgement, StringComparison.Ordinal);
        Assert.DoesNotContain("Accepted", acknowledgement, StringComparison.Ordinal);
        Assert.Contains("Removed", acknowledgement, StringComparison.Ordinal);
        await grain.DeactivateAsync();
        var recovered = await grain.ReadAsync();
        Assert.Empty(recovered.Outbox);
        Assert.Equal(after.Outbox.Revision, recovered.Outbox.Revision);
        Assert.Equal(after.Outbox.Epoch, recovered.Outbox.Epoch);
        Assert.Equal(1, recovered.Outbox.LatestSequenceNumber);
        await grain.ReceiveAsync(Token(2), "next", TestContext.Current.CancellationToken);
        Assert.Equal(2, Assert.Single((await grain.ReadAsync()).Outbox.Envelopes).Id.SequenceNumber);
    }

    [Fact]
    public async Task The_processor_accessor_exposes_pending_messages_before_the_write_is_acknowledged()
    {
        var grain = fixture.NewGrain();
        await grain.ReadAsync();
        var storage = fixture.Storage.For(grain.GetGrainId());
        await using var gate = storage.PauseNextWrite();
        var receive = grain.ReceiveAsync(Token(1), "pending", TestContext.Current.CancellationToken);
        gate.Observe(receive);
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("pending", Assert.Single(await grain.ReadDispatchableAsync()).Text);
        Assert.Empty(storage.Writes);
        gate.Release();
        Assert.True(await receive.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Single(await grain.ReadDispatchableAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_write_preserves_later_mutations_in_the_current_view_and_the_next_journal_batch(bool compact)
    {
        await using var session = await fixture.NewSessionAsync();
        var storage = fixture.Storage.For(session.Id);
        storage.CompactNext = compact;
        session.Outbox.Add(new OrderEvent("first-batch"));
        var first = session.Outbox.AsImmutable();
        await using var gate = storage.PauseNextWrite();
        var write = session.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask();
        gate.Observe(write);
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        session.Outbox.Add(new OrderEvent("later-batch"));
        var pending = session.Outbox.AsImmutable();
        Assert.Single(first);
        Assert.Equal(2, pending.Count);
        gate.Release();
        await write.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Same(pending, session.Outbox.AsImmutable());
        Assert.Equal(2, session.Outbox.AsImmutable().Count);
        Assert.DoesNotContain("later-batch", storage.Writes[0], StringComparison.Ordinal);

        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Same(pending, session.Outbox.AsImmutable());
        Assert.Contains("later-batch", storage.Writes[1], StringComparison.Ordinal);
        await session.DisposeAsync();
        await using var recovered = await fixture.NewSessionAsync(session.Id);
        Assert.Equal(pending, recovered.Outbox.AsImmutable());
        Assert.Equal(pending.Envelopes, recovered.Outbox.Envelopes);
    }

    [Theory]
    [InlineData(JournalFailure.BeforeCommit, 0)]
    [InlineData(JournalFailure.AfterCommit, 1)]
    public async Task An_uncertain_write_recovers_all_components_at_the_same_boundary(JournalFailure failure, int expected)
    {
        var grain = fixture.NewGrain();
        var initial = await grain.ReadAsync();
        fixture.Storage.For(grain.GetGrainId()).NextFailure = failure;
        var token = Token(1);

        await Assert.ThrowsAsync<IOException>(() =>
            grain.ReceiveAsync(token, "uncertain", TestContext.Current.CancellationToken));
        var recovered = await grain.ReadAsync();

        Assert.NotEqual(initial.Activation, recovered.Activation);
        Assert.Equal(expected, recovered.Business.Accepted);
        Assert.Equal(expected, recovered.Outbox.Count);
        Assert.Equal(expected == 1 ? token : null, recovered.Tracker.LatestOutbox(token.Sender));
        Assert.Equal(expected == 0, await grain.ReceiveAsync(token, "retry", TestContext.Current.CancellationToken));
        Assert.Equal(1, (await grain.ReadAsync()).Business.Accepted);
    }

    [Fact]
    public async Task Noncontiguous_acknowledgements_and_compaction_preserve_order_and_identity()
    {
        var grain = fixture.NewGrain();
        await grain.ReceiveAsync(Token(1), "one", TestContext.Current.CancellationToken);
        await grain.ReceiveAsync(Token(2), "two", TestContext.Current.CancellationToken);
        await grain.ReceiveAsync(Token(3), "three", TestContext.Current.CancellationToken);
        var initial = await grain.ReadAsync();
        await grain.AcknowledgeAsync([initial.Outbox.Envelopes[0].Id, initial.Outbox.Envelopes[2].Id], TestContext.Current.CancellationToken);
        var acknowledged = await grain.ReadAsync();
        var storage = fixture.Storage.For(grain.GetGrainId());
        storage.CompactNext = true;

        await grain.EvictAsync(DateTimeOffset.MinValue, TestContext.Current.CancellationToken);
        Assert.Contains("snapshot", storage.Writes[^1], StringComparison.Ordinal);
        await grain.DeactivateAsync();
        var recovered = await grain.ReadAsync();

        Assert.Equal(acknowledged.Outbox, recovered.Outbox);
        Assert.Equal(initial.Outbox.Envelopes[1], Assert.Single(recovered.Outbox.Envelopes));
        Assert.Equal(3, recovered.Outbox.LatestSequenceNumber);
        Assert.Equal(initial.Tracker, recovered.Tracker);
        Assert.Equal(initial.Business, recovered.Business);
        await grain.ReceiveAsync(Token(4), "four", TestContext.Current.CancellationToken);
        Assert.Equal(new long[] { 2, 4 }, (await grain.ReadAsync()).Outbox.Envelopes.Select(item => item.Id.SequenceNumber));
    }

    [Fact]
    public async Task A_failed_clear_retires_the_activation_before_another_write_can_drop_pending_messages()
    {
        var grain = fixture.NewGrain();
        await grain.ReceiveAsync(Token(1), "must-survive", TestContext.Current.CancellationToken);
        var saved = await grain.ReadAsync();
        fixture.Storage.For(grain.GetGrainId()).NextFailure = JournalFailure.BeforeCommit;

        await Assert.ThrowsAsync<IOException>(() => grain.ClearOutboxAsync(TestContext.Current.CancellationToken));
        var recovered = await grain.ReadAsync();

        Assert.NotEqual(saved.Activation, recovered.Activation);
        Assert.Equal(saved.Outbox, recovered.Outbox);
        await grain.ReceiveAsync(Token(2), "next", TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "must-survive", "next" }, (await grain.ReadAsync()).Outbox.Select(item => item.Text));
    }

    [Fact]
    public async Task Clear_preserves_the_sequence_space_through_a_compacted_empty_snapshot()
    {
        var grain = fixture.NewGrain();
        await grain.ReceiveAsync(Token(1), "one", TestContext.Current.CancellationToken);
        var initial = await grain.ReadAsync();
        fixture.Storage.For(grain.GetGrainId()).CompactNext = true;

        await grain.ClearOutboxAsync(TestContext.Current.CancellationToken);
        var cleared = await grain.ReadAsync();
        await grain.DeactivateAsync();
        var recovered = await grain.ReadAsync();

        Assert.Empty(recovered.Outbox);
        Assert.Equal(cleared.Outbox, recovered.Outbox);
        Assert.Equal(initial.Outbox.Epoch, recovered.Outbox.Epoch);
        Assert.Equal(initial.Outbox.LatestSequenceNumber, recovered.Outbox.LatestSequenceNumber);
    }

    [Fact]
    public async Task Tracker_replay_preserves_provider_positions_and_original_received_times()
    {
        var id = new JournalId($"tracker/{Guid.NewGuid():N}");
        var clock = new ManualTimeProvider(Token(1).Timestamp);
        await using var session = await fixture.NewSessionAsync(id, clock);
        var first = new StreamCursor("orders", new EventSequenceToken(7), "provider-a");
        Assert.True(session.Tracker.TryAcceptMessage(first));
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var cutoff = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(session.Tracker.TryAcceptMessage(new StreamCursor("orders", new EventSequenceToken(2), "provider-b")));
        Assert.True(session.Tracker.TryAcceptMessage(Token(1)));
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var saved = session.Tracker.AsImmutable();
        Assert.DoesNotContain("provider-a", fixture.Storage.For(id).Writes[^1], StringComparison.Ordinal);
        await session.DisposeAsync();
        clock.Advance(TimeSpan.FromDays(1));
        await using var recovered = await fixture.NewSessionAsync(id, clock);

        Assert.Equal(saved, recovered.Tracker.AsImmutable());
        Assert.False(recovered.Tracker.TryAcceptMessage(first));
        recovered.Tracker.Evict(cutoff);
        await recovered.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await recovered.DisposeAsync();
        await using var evicted = await fixture.NewSessionAsync(id, clock);
        Assert.Null(evicted.Tracker.AsImmutable().LatestStream("provider-a", "orders"));
        Assert.NotNull(evicted.Tracker.AsImmutable().LatestStream("provider-b", "orders"));
        Assert.Equal(Token(1), evicted.Tracker.AsImmutable().LatestOutbox(Token(1).Sender));
    }

    private static OutboxSequenceToken Token(long sequence) => new(
        sequence,
        GrainId.Create("prototype-sender", "source"),
        DateTimeOffset.Parse("2026-09-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-09-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
}

public sealed class JournalingPrototypeFixture : IAsyncLifetime
{
    private InProcessTestCluster cluster = null!;
    public RecordingJournalStorageProvider Storage { get; } = new();
    public DeliveredMessages Delivered { get; } = new();

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddMessagingJournalingPrototype();
            silo.UseInMemoryReminderService();
            silo.ConfigureServices(services =>
            {
                services.AddSingleton<IJournalStorageProvider>(Storage);
                services.AddSingleton(Delivered);
            });
        });
        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public INamedComponentsGrain NewNamedGrain() => cluster.Client.GetGrain<INamedComponentsGrain>(Guid.NewGuid());
    public IJournaledOrderGrain NewGrain() => cluster.Client.GetGrain<IJournaledOrderGrain>(Guid.NewGuid());
    public IImmediateOutboxGrain NewImmediateGrain() => cluster.Client.GetGrain<IImmediateOutboxGrain>(Guid.NewGuid());
    public async Task<JournalSession> NewSessionAsync(JournalId id = default, TimeProvider? time = null)
    {
        var services = cluster.Silos.Single().ServiceProvider;
        var journalId = id.IsDefault ? new JournalId($"prototype/{Guid.NewGuid():N}") : id;
        var manager = services.GetRequiredService<IJournaledStateManagerFactory>().Create(journalId);
        var tracker = new DurableMessageTracker("tracker", manager, time ?? TimeProvider.System,
            services.GetRequiredService<JournalCodec<TrackerOperation>>());
        var outbox = new DurableOutbox<OrderEvent>("outbox", manager, time ?? TimeProvider.System,
            services.GetRequiredService<JournalCodec<OutboxOperation<OrderEvent>>>());
        try
        {
            await manager.InitializeAsync(TestContext.Current.CancellationToken);
            return new JournalSession(journalId, manager, tracker, outbox);
        }
        catch
        {
            await manager.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => cluster.DisposeAsync();
}

public sealed class JournalSession(
    JournalId id,
    IJournaledStateManager manager,
    IDurableMessageTracker tracker,
    IDurableOutbox<OrderEvent> outbox) : IAsyncDisposable
{
    private bool disposed;
    public JournalId Id => id;
    public IJournaledStateManager Manager => manager;
    public IDurableMessageTracker Tracker => tracker;
    public IDurableOutbox<OrderEvent> Outbox => outbox;

    public async ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            await manager.DisposeAsync();
        }
    }
}
