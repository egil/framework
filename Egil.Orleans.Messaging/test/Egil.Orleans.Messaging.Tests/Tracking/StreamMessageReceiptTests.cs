using System.Text.Json;
using Orleans.Providers.Streams.Common;
using Orleans.Serialization;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests.Tracking;

public sealed class StreamMessageReceiptTests
{
    [Fact]
    public void Retry_at_a_new_native_position_keeps_the_original_snapshot()
    {
        var delivery = Cursor(sequence: 12, position: 100);
        Assert.True(CreateTracker().TryAcceptMessage(delivery, out var accepted));

        Assert.False(accepted.TryAcceptMessage(delivery with { Token = new EventSequenceToken(102) }, out var duplicate));

        Assert.Same(accepted, duplicate);
        Assert.Equal(delivery, duplicate.LatestStream("events", delivery.StreamId!.Value));
    }

    [Fact]
    public void Higher_then_lower_sender_sequences_are_independently_accepted()
    {
        var high = Cursor(12, 100);
        var low = Cursor(11, 101);
        Assert.True(CreateTracker().TryAcceptMessage(high, out var tracker));

        Assert.True(tracker.TryAcceptMessage(low, out tracker));

        Assert.False(tracker.TryAcceptMessage(high with { Token = new EventSequenceToken(102) }, out _));
        Assert.False(tracker.TryAcceptMessage(low with { Token = new EventSequenceToken(103) }, out _));
        Assert.Equal(low, tracker.LatestStream("events", low.StreamId!.Value));
    }

    [Fact]
    public void Unseen_older_provider_position_and_epoch_do_not_move_the_checkpoint_backwards()
    {
        var high = Cursor(12, 100);
        Assert.True(CreateTracker().TryAcceptMessage(high, out var tracker));
        var older = Cursor(11, 99) with { OutboxToken = high.OutboxToken! with { Epoch = DateTimeOffset.UnixEpoch.AddDays(-1) } };

        Assert.True(tracker.TryAcceptMessage(older, out tracker));

        Assert.Equal(high, tracker.LatestStream("events", high.StreamId!.Value));
        Assert.False(tracker.TryAcceptMessage(older, out _));
    }

    [Fact]
    public void Sender_timestamp_and_trace_do_not_change_logical_identity()
    {
        var original = Cursor(1, 1);
        Assert.True(CreateTracker().TryAcceptMessage(original, out var tracker));
        var changed = original with { OutboxToken = original.OutboxToken! with { Timestamp = DateTimeOffset.MaxValue, TraceParent = "different" } };

        Assert.False(tracker.TryAcceptMessage(changed, out var next));

        Assert.Same(tracker, next);
        Assert.True(tracker.TryAcceptMessage(original with { OutboxToken = original.OutboxToken! with { Sender = GrainId.Create("sender", "other") } }, out _));
        Assert.True(tracker.TryAcceptMessage(original with { OutboxToken = original.OutboxToken! with { Epoch = DateTimeOffset.UnixEpoch.AddDays(1) } }, out _));
    }

    [Fact]
    public void Null_native_tokens_still_have_exact_receipts()
    {
        var delivery = Cursor(1, null);
        Assert.True(CreateTracker().TryAcceptMessage(delivery, out var tracker));

        Assert.False(tracker.TryAcceptMessage(delivery, out var next));

        Assert.Same(tracker, next);
        Assert.Null(tracker.LatestStream("events", delivery.StreamId!.Value));
        Assert.True(tracker.TryAcceptMessage(delivery with { OutboxToken = null }, out var raw));
        Assert.Same(tracker, raw);
    }

    [Fact]
    public void Full_stream_sources_allow_independent_fanout_and_reject_ambiguous_lookups()
    {
        var first = Cursor(1, 100);
        var second = first with { StreamId = StreamId.Create("orders", "second"), Token = new EventSequenceToken(1) };
        var otherProvider = first with { ProviderName = "other", Token = new EventSequenceToken(2) };
        Assert.True(CreateTracker().TryAcceptMessage(first, out var tracker));
        Assert.True(tracker.TryAcceptMessage(second, out tracker));
        Assert.True(tracker.TryAcceptMessage(otherProvider, out tracker));

        Assert.Null(tracker.LatestStream("events", "orders"));
        Assert.Null(tracker.LatestStream("orders"));
        Assert.Null(tracker.LatestStream(first.StreamId!.Value));
        Assert.Equal(first, tracker.LatestStream("events", first.StreamId.Value));
        Assert.Equal(second, tracker.LatestStream("events", second.StreamId!.Value));
        Assert.Equal(otherProvider.Token, tracker.LatestStreamSequenceToken("other", first.StreamId.Value));
        Assert.False(tracker.TryAcceptMessage(second, out _));
    }

    [Fact]
    public void Exact_lookup_does_not_guess_a_legacy_namespace_mapping()
    {
        var legacy = new StreamCursor("orders", new EventSequenceToken(9), "events");
        Assert.True(CreateTracker().TryAcceptMessage(legacy, out var tracker));

        Assert.Null(tracker.LatestStream("events", StreamId.Create("orders", "first")));
        Assert.Null(tracker.LatestStream(StreamId.Create("orders", "first")));
        Assert.Equal(legacy, tracker.LatestStream("events", "orders"));
    }

    [Fact]
    public void Receipt_retention_is_independent_of_checkpoint_and_rpc_retention()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var first = Cursor(1, 1);
        var tracker = CreateTracker();
        tracker.RegisterTimeProvider(clock);
        Assert.True(tracker.TryAcceptMessage(first, out tracker));
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(tracker.TryAcceptMessage(Cursor(2, 2), out tracker));
        Assert.True(tracker.TryAcceptMessage(first.OutboxToken!, out tracker));
        tracker = tracker.EvictOutboxes(DateTimeOffset.MaxValue).Evict(first.OutboxToken!.Sender, DateTimeOffset.MaxValue);
        Assert.False(tracker.TryAcceptMessage(first, out _));

        var evicted = tracker.Evict("events", first.StreamId!.Value, DateTimeOffset.UnixEpoch);

        Assert.Equal(new EventSequenceToken(2), evicted.LatestStreamSequenceToken("events", first.StreamId.Value));
        Assert.True(evicted.TryAcceptMessage(first, out _));
        Assert.False(evicted.TryAcceptMessage(Cursor(2, 2), out _));
        Assert.False(tracker.TryAcceptMessage(first, out _));
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("stream")]
    [InlineData("namespace")]
    [InlineData("all-streams")]
    [InlineData("all")]
    public void Explicit_eviction_removes_only_matching_receipts(string scope)
    {
        var first = Cursor(1, null);
        var otherStream = first with { StreamId = StreamId.Create("orders", "other") };
        var otherProvider = first with { ProviderName = "other" };
        Assert.True(CreateTracker().TryAcceptMessage(first, out var tracker));
        Assert.True(tracker.TryAcceptMessage(otherStream, out tracker));
        Assert.True(tracker.TryAcceptMessage(otherProvider, out tracker));

        var evicted = scope switch
        {
            "provider" => tracker.Evict("events", first.StreamId!.Value, DateTimeOffset.MaxValue),
            "stream" => tracker.Evict(first.StreamId!.Value, DateTimeOffset.MaxValue),
            "namespace" => tracker.Evict("orders", DateTimeOffset.MaxValue),
            "all-streams" => tracker.EvictStreams(DateTimeOffset.MaxValue),
            _ => tracker.Evict(DateTimeOffset.MaxValue)
        };

        Assert.True(evicted.TryAcceptMessage(first, out _));
        Assert.Equal(scope != "provider", evicted.TryAcceptMessage(otherProvider, out _));
        Assert.Equal(scope is not ("provider" or "stream"), evicted.TryAcceptMessage(otherStream, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Snapshot_serialization_preserves_lossless_sources_receipts_and_acceptance_time(bool binary)
    {
        var id = StreamId.Create("orders"u8, new byte[] { 0, 255, 128, 47 });
        var delivery = Cursor(1, 9) with { StreamId = id };
        var tracker = CreateTracker();
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        tracker.RegisterTimeProvider(clock);
        tracker.Configure(options => options.RetentionPeriod = TimeSpan.FromHours(1));
        Assert.True(tracker.TryAcceptMessage(delivery, out tracker));
        Assert.True(tracker.TryAcceptMessage(Cursor(2, null), out tracker));
        Assert.True(tracker.TryAcceptMessage(delivery.OutboxToken!, out tracker));
        using var services = new ServiceCollection().AddSerializer(builder => builder.AddAssembly(typeof(MessageTracker).Assembly)).BuildServiceProvider();
        var serializer = services.GetRequiredService<Serializer>();

        var loaded = binary ? serializer.Deserialize<MessageTracker>(serializer.SerializeToArray(tracker))
            : JsonSerializer.Deserialize<MessageTracker>(JsonSerializer.Serialize(tracker, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        Assert.NotNull(loaded);
        loaded.Configure(options =>
        {
            options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity;
            options.RetentionPeriod = TimeSpan.FromHours(1);
        });
        loaded.RegisterTimeProvider(clock);
        Assert.Equal(tracker, loaded);
        Assert.Equal(tracker.GetHashCode(), loaded.GetHashCode());
        Assert.Equal(id, loaded.LatestStream("events", id)?.StreamId);
        Assert.False(loaded.TryAcceptMessage(delivery with { Token = new EventSequenceToken(10) }, out _));
        Assert.False(loaded.TryAcceptMessage(Cursor(2, null), out _));
        Assert.True(loaded.Evict(DateTimeOffset.UnixEpoch).TryAcceptMessage(delivery, out _));
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(loaded.TryAcceptMessage(delivery, out var expired));
        Assert.Null(expired.LatestOutbox(delivery.OutboxToken!.Sender));
        Assert.True(expired.TryAcceptMessage(Cursor(2, null), out _));
    }

    [Fact]
    public void Invalid_explicit_source_metadata_is_rejected()
    {
        var valid = Cursor(1, 1);
        var tracker = CreateTracker();
        Assert.Throws<ArgumentException>(() => tracker.TryAcceptMessage(valid with { StreamId = null }, out _));
        Assert.Throws<ArgumentException>(() => tracker.TryAcceptMessage(valid with { ProviderName = null }, out _));
        Assert.Throws<ArgumentException>(() => tracker.TryAcceptMessage(valid with { StreamNamespace = "different" }, out _));
        Assert.Throws<ArgumentNullException>(() => tracker.TryAcceptMessage(valid with { StreamNamespace = null!, StreamId = default(StreamId) }, out _));
    }

    private static MessageTracker CreateTracker()
    {
        var tracker = new MessageTracker();
        tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity);
        return tracker;
    }

    private static StreamCursor Cursor(long sequence, long? position) => new("orders", position is { } number ? new EventSequenceToken(number) : null, "events")
    {
        StreamId = StreamId.Create("orders", "first"),
        OutboxToken = new(sequence, GrainId.Create("sender", "one"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
    };
}
