using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;

namespace Egil.Orleans.EventSourcing.Tests.AzureStorage;

public class AzureAppendBlobEventStorageTest(AppHostFixture fixture) : IClassFixture<AppHostFixture>
{
    [Fact]
    public async Task Append_and_read_back_logs()
    {
        var blobClient = await fixture.GetAppendBlobClientAsync();
        var sut = new AzureAppendBlobEventStorage<MyEventBase>(
            blobClient,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            fixture.LoggerFactory.CreateLogger<AzureAppendBlobEventStorage<MyEventBase>>());
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
        var blobClient = await fixture.GetAppendBlobClientAsync();
        var sut = new AzureAppendBlobEventStorage<MyEventBase>(
            blobClient,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            fixture.LoggerFactory.CreateLogger<AzureAppendBlobEventStorage<MyEventBase>>());
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
    public async Task Can_add_more_than_AppendBlobMaxBlocks_logs()
    {
        var blobClient = new AppendBlobClientWrapper(await fixture.GetAppendBlobClientAsync(), 10_000, 10);
        var sut = new AzureAppendBlobEventStorage<MyEventBase>(
            blobClient,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            fixture.LoggerFactory.CreateLogger<AzureAppendBlobEventStorage<MyEventBase>>());
        var events = Enumerable
            .Range(0, blobClient.AppendBlobMaxBlocks + 1)
            .Select(i => new MyEvent($"Event {i}", i, DateTimeOffset.UtcNow))
            .ToList();

        var appended = await sut.AppendEventsAsync(events, TestContext.Current.CancellationToken);
        var readEvents = await sut
            .ReadEventsAsync(fromVersion: 0, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(events.Count, appended);
        Assert.Equal(events, readEvents, new AssertEquivalenceComparer<MyEventBase>(strict: true));
    }

    [Fact]
    public async Task Can_add_more_than_AppendBlobMaxAppendBlockBytes_logs()
    {
        var blobClient = new AppendBlobClientWrapper(await fixture.GetAppendBlobClientAsync(), 10_000, 10);
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var singleEventBytes = JsonSerializer.SerializeToUtf8Bytes(new MyEvent($"Event X", 1, DateTimeOffset.UtcNow), serializerOptions);
        var sut = new AzureAppendBlobEventStorage<MyEventBase>(
            blobClient,
            serializerOptions,
            fixture.LoggerFactory.CreateLogger<AzureAppendBlobEventStorage<MyEventBase>>());
        var events = Enumerable
            .Range(0, blobClient.AppendBlobMaxAppendBlockBytes / singleEventBytes.Length)
            .Select(i => new MyEvent($"Event X", i, DateTimeOffset.UtcNow))
            .ToList();

        var appended = await sut.AppendEventsAsync(events, TestContext.Current.CancellationToken);
        var readEvents = await sut
            .ReadEventsAsync(fromVersion: 0, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(events.Count, appended);
        Assert.Equal(events, readEvents, new AssertEquivalenceComparer<MyEventBase>(strict: true));
    }

    [Fact]
    public async Task Skips_single_events_larger_than_AppendBlobMaxAppendBlockBytes()
    {
        var blobClient = new AppendBlobClientWrapper(await fixture.GetAppendBlobClientAsync(), 1_000, 10);
        var sut = new AzureAppendBlobEventStorage<MyEventBase>(
            blobClient,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            fixture.LoggerFactory.CreateLogger<AzureAppendBlobEventStorage<MyEventBase>>());
        var normalEvent = new MyEvent("Short", 42, DateTimeOffset.UtcNow);
        var largeEvent = new MyEvent(new string('x', 1_001), 42, DateTimeOffset.UtcNow);

        var normalAndLargeResult = await sut.AppendEventsAsync([normalEvent, largeEvent], TestContext.Current.CancellationToken);
        var largeOnlyResult = await sut.AppendEventAsync(largeEvent, TestContext.Current.CancellationToken);

        var readEvents = await sut
            .ReadEventsAsync(fromVersion: 0, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, normalAndLargeResult);
        Assert.Equal(0, largeOnlyResult);
        Assert.Equal([normalEvent], readEvents, new AssertEquivalenceComparer<MyEventBase>(strict: true));
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

        public override Task<Response<BlobAppendInfo>> AppendBlockAsync(Stream content, AppendBlobAppendBlockOptions? options = null, CancellationToken cancellationToken = default)
            => inner.AppendBlockAsync(content, options, cancellationToken);

        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(BlobDownloadOptions? options = null, CancellationToken cancellationToken = default)
            => inner.DownloadStreamingAsync(options, cancellationToken);
    }
}
