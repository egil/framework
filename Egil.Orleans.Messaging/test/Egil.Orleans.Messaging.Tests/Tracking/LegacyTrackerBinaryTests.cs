using System.Collections.Immutable;
using Orleans.Providers.Streams.Common;
using Orleans.Serialization;

namespace Egil.Orleans.Messaging.Tests.Tracking;

public sealed class LegacyTrackerBinaryTests
{
    [Fact]
    public void Old_field_layout_reads_without_inventing_full_sources_or_receipts()
    {
        var received = DateTimeOffset.UnixEpoch;
        var sender = GrainId.Create("sender", "legacy");
        var snapshot = new LegacyTrackerSnapshot
        {
            Streams = ImmutableDictionary<LegacyStreamSource, LegacyStreamEntry>.Empty.Add(new("orders", "events"),
                new(new("orders", new EventSequenceToken(7), "events"), received)),
            Outboxes = ImmutableDictionary<GrainId, LegacyOutboxEntry>.Empty.Add(sender, new(received, 1, received, received))
        };
        using var services = new ServiceCollection().AddSerializer(builder =>
        {
            builder.AddAssembly(typeof(MessageTracker).Assembly);
            builder.AddAssembly(typeof(LegacyTrackerBinaryTests).Assembly);
        }).BuildServiceProvider();
        var serializer = services.GetRequiredService<Serializer>();
        // Typed codecs omit expected type names; this fixture writes only the historical IDs,
        // including the old nested cursor/source layouts, to exercise truly absent new fields.
        var bytes = serializer.SerializeToArray(snapshot);

        var loaded = serializer.Deserialize<MessageTracker>(bytes);

        Assert.NotNull(loaded);
        Assert.Equal(new EventSequenceToken(7), loaded.LatestStreamSequenceToken("events", "orders"));
        Assert.Null(loaded.LatestStream("events", StreamId.Create("orders", "one")));
        Assert.Equal(new OutboxSequenceToken(1, sender, received, received), loaded.LatestOutbox(sender));
        var delivery = new StreamCursor("orders", new EventSequenceToken(8), "events")
        {
            StreamId = StreamId.Create("orders", "one"), OutboxToken = new(1, sender, received, received)
        };
        loaded.RegisterTimeProvider(new TimeProviderExtensions.ManualTimeProvider(received));
        Assert.True(loaded.TryAcceptMessage(delivery, out var accepted));
        Assert.False(accepted.TryAcceptMessage(delivery, out _));
        Assert.True(accepted.Evict(received).TryAcceptMessage(delivery, out _));
    }
}

[GenerateSerializer]
internal sealed class LegacyTrackerSnapshot
{
    [Id(0)] public ImmutableDictionary<LegacyStreamSource, LegacyStreamEntry> Streams { get; init; } = ImmutableDictionary<LegacyStreamSource, LegacyStreamEntry>.Empty;
    [Id(1)] public ImmutableDictionary<GrainId, LegacyOutboxEntry> Outboxes { get; init; } = ImmutableDictionary<GrainId, LegacyOutboxEntry>.Empty;
}
[GenerateSerializer]
internal readonly record struct LegacyStreamSource([property: Id(0)] string Namespace, [property: Id(1)] string? Provider);
[GenerateSerializer]
internal readonly record struct LegacyStreamEntry([property: Id(0)] LegacyStreamCursor Cursor, [property: Id(1)] DateTimeOffset Received);
[GenerateSerializer]
internal sealed record LegacyStreamCursor([property: Id(0)] string Namespace, [property: Id(1)] global::Orleans.Streams.StreamSequenceToken? Token, [property: Id(2)] string? Provider);
[GenerateSerializer]
internal readonly record struct LegacyOutboxEntry([property: Id(0)] DateTimeOffset Epoch, [property: Id(1)] long Sequence,
    [property: Id(2)] DateTimeOffset Received, [property: Id(3)] DateTimeOffset Timestamp);
