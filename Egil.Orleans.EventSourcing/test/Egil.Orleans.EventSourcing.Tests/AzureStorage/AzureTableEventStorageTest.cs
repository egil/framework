using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Egil.Orleans.EventSourcing.AzureStorage.TableStorage;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Egil.Orleans.EventSourcing.Tests.AzureStorage;

public class AzureTableEventStorageTest(AppHostFixture fixture) : IClassFixture<AppHostFixture>
{
    [Fact]
    public async Task Append_and_read_back_logs()
    {
        var partition = await fixture.GetPartitionAsync();
        var sut = new StreamstoneEventStorage<MyEventBase>(
            partition,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            fixture.LoggerFactory.CreateLogger<StreamstoneEventStorage<MyEventBase>>());
        var events = Enumerable
            .Range(0, 5)
            .Select(i => new MyEvent($"Event {i}", i, DateTimeOffset.UtcNow))
            .ToList();

        var singleAppended = await sut.AppendEventAsync(events[0], TestContext.Current.CancellationToken);
        var multipleAppended = await sut.AppendEventsAsync(events[1..], TestContext.Current.CancellationToken);
        var readEvents = await sut
            .ReadEventsAsync(fromVersion: 0, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(events.Count, singleAppended + multipleAppended);
        Assert.Equal(events, readEvents, new AssertEquivalenceComparer<MyEventBase>(strict: true));
    }

    [Fact]
    public async Task Read_skips_until_fromVersion()
    {
        var partition = await fixture.GetPartitionAsync();
        var sut = new StreamstoneEventStorage<MyEventBase>(
            partition,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            fixture.LoggerFactory.CreateLogger<StreamstoneEventStorage<MyEventBase>>());
        var events = Enumerable
            .Range(0, 5)
            .Select(i => new MyEvent($"Event {i}", i, DateTimeOffset.UtcNow))
            .ToList();

        var appended = await sut.AppendEventsAsync(events, TestContext.Current.CancellationToken);
        var readEvents = await sut
            .ReadEventsAsync(fromVersion: 3, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(events.Count, appended);
        Assert.Equal(events[2..], readEvents, new AssertEquivalenceComparer<MyEventBase>(strict: true));
    }

    [Fact]
    public async Task Read_returns_nothing_fromVersion_gt_max_version()
    {
        var partition = await fixture.GetPartitionAsync();
        var sut = new StreamstoneEventStorage<MyEventBase>(
            partition,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            fixture.LoggerFactory.CreateLogger<StreamstoneEventStorage<MyEventBase>>());
        var events = Enumerable
            .Range(0, 2)
            .Select(i => new MyEvent($"Event {i}", i, DateTimeOffset.UtcNow))
            .ToList();

        var appended = await sut.AppendEventsAsync(events, TestContext.Current.CancellationToken);
        var readEvents = await sut
            .ReadEventsAsync(fromVersion: appended + 1, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(readEvents);
    }

    [Fact]
    public async Task Read_returns_latest_when_fromVersion_eq_version()
    {
        var partition = await fixture.GetPartitionAsync();
        var sut = new StreamstoneEventStorage<MyEventBase>(
            partition,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            fixture.LoggerFactory.CreateLogger<StreamstoneEventStorage<MyEventBase>>());
        var events = Enumerable
            .Range(0, 2)
            .Select(i => new MyEvent($"Event {i}", i, DateTimeOffset.UtcNow))
            .ToList();

        var appended = await sut.AppendEventsAsync(events, TestContext.Current.CancellationToken);
        var readEvents = await sut
            .ReadEventsAsync(fromVersion: appended, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        var latestEvent = Assert.Single(readEvents);
        Assert.Equivalent(events[^1], latestEvent, strict: true);
    }

    [JsonDerivedType(typeof(MyEvent), nameof(MyEvent))]
    private abstract record class MyEventBase(DateTimeOffset Timestamp);

    private record class MyEvent(string Name, int Age, DateTimeOffset Timestamp) : MyEventBase(Timestamp);

    // Wrapper around an AppendBlobClient to override the AppendBlobMaxAppendBlockBytes and AppendBlobMaxBlocks properties
    // to speed up testing.
    private sealed class AppendBlobClientWrapper(AppendBlobClient inner, int appendBlobMaxAppendBlockBytes, int appendBlobMaxBlocks) : AppendBlobClient
    {
        public override int AppendBlobMaxAppendBlockBytes => appendBlobMaxAppendBlockBytes;

        public override int AppendBlobMaxBlocks => appendBlobMaxBlocks;

        public override Task<Response<BlobContentInfo>> CreateAsync(AppendBlobCreateOptions options, CancellationToken cancellationToken = default)
            => inner.CreateAsync(options, cancellationToken);

        public override Task<Response<BlobContentInfo>> CreateIfNotExistsAsync(AppendBlobCreateOptions options, CancellationToken cancellationToken = default)
            => inner.CreateIfNotExistsAsync(options, cancellationToken);

        public override Task<Response<bool>> ExistsAsync(CancellationToken cancellationToken = default)
            => inner.ExistsAsync(cancellationToken);

        public override Task<Response<BlobAppendInfo>> AppendBlockAsync(Stream content, AppendBlobAppendBlockOptions? options = null, CancellationToken cancellationToken = default)
            => inner.AppendBlockAsync(content, options, cancellationToken);

        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(BlobDownloadOptions? options = null, CancellationToken cancellationToken = default)
            => inner.DownloadStreamingAsync(options, cancellationToken);
    }
}
