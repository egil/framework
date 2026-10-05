using System.Buffers;
using System.Text.Json;
using Orleans.Journaling;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class LegacyJournalTests(JournalingPrototypeFixture fixture) : IClassFixture<JournalingPrototypeFixture>
{
    [Theory]
    [InlineData("10.3.1-append.jsonl")]
    [InlineData("10.3.1-snapshot.jsonl")]
    public async Task Old_preview_journals_preserve_identities_and_receiver_progress(string file)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", file), TestContext.Current.CancellationToken);
        var id = new JournalId($"legacy/{Guid.NewGuid():N}");
        var storage = (RecordingJournalStorage)fixture.Storage.CreateStorage(id);
        await storage.AppendAsync(new ReadOnlySequence<byte>(bytes), TestContext.Current.CancellationToken);
        // JSONL records are [streamId, ["set", {"Kind":"delta","Revision":"...",...}]].
        // [1][1] selects the final operation's recorded metadata instead of generating an identity.
        using var last = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes).TrimEnd().Split('\n')[^1]);
        var operation = last.RootElement[1][1];
        await using var recovered = await fixture.NewSessionAsync(id);
        var envelope = Assert.Single(recovered.Outbox.Envelopes);
        Assert.Equal("legacy-two", envelope.Message.Text);
        Assert.Equal(2, envelope.Id.SequenceNumber);
        Assert.Equal(2, recovered.Outbox.LatestSequenceNumber);
        Assert.Equal(operation.GetProperty("Revision").GetGuid(), recovered.Outbox.Revision);
        Assert.Equal(operation.GetProperty("Epoch").GetDateTimeOffset(), recovered.Outbox.Epoch);
        Assert.Equal(recovered.Outbox.Epoch, envelope.Id.Epoch);
        Assert.Equal(recovered.Outbox.Epoch, envelope.Id.Timestamp);
        var expectedJson = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", file.Replace(".jsonl", ".expected.json", StringComparison.Ordinal)), TestContext.Current.CancellationToken);
        var expected = JsonSerializer.Deserialize<OrderView>(expectedJson)!;
        Assert.Equal(expected.Outbox, recovered.Outbox.AsImmutable());
        Assert.Equal(expected.Tracker, recovered.Tracker.AsImmutable());
        Assert.NotNull(recovered.Tracker.LatestStream("legacy-provider", StreamId.Create("orders", "one")));
        recovered.Tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity);
        var token = recovered.Tracker.LatestOutbox(GrainId.Create("legacy-sender", "source"));
        Assert.NotNull(token);
        Assert.Equal(7, token.SequenceNumber);
        Assert.False(recovered.Tracker.TryAcceptMessage(token));
        var tracker = recovered.Tracker.AsImmutable();
        var outbox = recovered.Outbox.AsImmutable();
        storage.CompactNext = true;
        await recovered.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await recovered.DisposeAsync();
        await using var compacted = await fixture.NewSessionAsync(id);
        Assert.Equal(tracker, compacted.Tracker.AsImmutable());
        Assert.Equal(outbox, compacted.Outbox.AsImmutable());
    }
}
