using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Storage;

namespace Egil.Orleans.Testing.Tests;

public class StorageObserverTests
{
    [Fact]
    public async Task WriteStateAsync_publishes_storage_operation_details()
    {
        using var harness = CreateHarness(new FakeGrainStorage());
        var grainId = GrainId.Create("test-grain", "write");
        var state = new TestGrainState<string> { ETag = "etag-1", State = "value" };
        var ct = TestContext.Current.CancellationToken;

        var collectTask = harness.Collector
            .GetStorageOperationsAsync(cancellationToken: ct)
            .Where(op => op.Kind == StorageOperationKind.Write
                && op.GrainId == grainId
                && op.StorageName == "Default"
                && op.StateName == "state"
                && Equals(op.State, "value")
                && op.Etag == "etag-1")
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        await harness.Storage.WriteStateAsync("state", grainId, state);
        var collected = await collectTask.WaitAsync(TimeSpan.FromMilliseconds(250), ct);

        Assert.Single(collected);
        Assert.Equal(1, harness.Inner.WriteCount);
    }

    [Fact]
    public async Task ReadStateAsync_publishes_read_operation()
    {
        using var harness = CreateHarness(new FakeGrainStorage());
        var grainId = GrainId.Create("test-grain", "read");
        var state = new TestGrainState<string> { ETag = "etag-2", State = "value" };
        var ct = TestContext.Current.CancellationToken;

        var collectTask = harness.Collector
            .GetStorageOperationsAsync(cancellationToken: ct)
            .Where(op => op.Kind == StorageOperationKind.Read && op.GrainId == grainId)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        await harness.Storage.ReadStateAsync("state", grainId, state);
        var collected = await collectTask.WaitAsync(TimeSpan.FromMilliseconds(250), ct);

        Assert.Single(collected);
        Assert.Equal(1, harness.Inner.ReadCount);
    }

    [Fact]
    public async Task ClearStateAsync_does_not_publish_during_assertion_scope()
    {
        using var harness = CreateHarness(new FakeGrainStorage());
        var grainId = GrainId.Create("test-grain", "clear");
        var state = new TestGrainState<string> { ETag = "etag-3", State = "value" };
        var ct = TestContext.Current.CancellationToken;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var collectTask = harness.Collector
            .GetStorageOperationsAsync(cancellationToken: cts.Token)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        using (RequestContextScope.ForAssertion())
        {
            await harness.Storage.ClearStateAsync("state", grainId, state);
        }

        Assert.Equal(1, harness.Inner.ClearCount);

        // Cancel after a short delay to verify no event was published
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => collectTask.WaitAsync(TimeSpan.FromMilliseconds(250), ct));
    }

    [Fact]
    public void Participate_forwards_to_inner_lifecycle_participant()
    {
        using var harness = CreateHarness(new LifecycleFakeGrainStorage());
        var participant = Assert.IsAssignableFrom<ILifecycleParticipant<ISiloLifecycle>>(harness.Storage);

        participant.Participate(null!);

        Assert.Equal(1, ((LifecycleFakeGrainStorage)harness.Inner).ParticipateCount);
    }

    [Fact]
    public void Participate_ignores_non_participant_inner()
    {
        using var harness = CreateHarness(new FakeGrainStorage());
        var participant = Assert.IsAssignableFrom<ILifecycleParticipant<ISiloLifecycle>>(harness.Storage);

        participant.Participate(null!);
    }

    private class FakeGrainStorage : IGrainStorage
    {
        public int ClearCount { get; private set; }

        public int ReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        {
            ClearCount++;
            return Task.CompletedTask;
        }

        public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        {
            ReadCount++;
            return Task.CompletedTask;
        }

        public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        {
            WriteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class LifecycleFakeGrainStorage : FakeGrainStorage, ILifecycleParticipant<ISiloLifecycle>
    {
        public int ParticipateCount { get; private set; }

        public void Participate(ISiloLifecycle lifecycle)
        {
            ParticipateCount++;
        }
    }

    private sealed class TestGrainState<T> : IGrainState<T>
    {
        public string? ETag { get; set; }

        public bool RecordExists { get; set; }

        public T State { get; set; } = default!;
    }

    private static Harness CreateHarness(FakeGrainStorage inner, string storageName = "Default")
    {
        var builder = new TestSiloBuilder();
        var collector = new GrainActivityCollector();
        builder.Services.Add(ServiceDescriptor.KeyedSingleton(typeof(IGrainStorage), storageName, inner));

        var collectorBuilder = builder.AddGrainActivityCollector(collector);
        if (storageName == "Default")
        {
            collectorBuilder.CollectStorageActivityFromDefault();
        }
        else
        {
            collectorBuilder.CollectStorageActivityFrom(storageName);
        }

        var provider = builder.Services.BuildServiceProvider();
        var storage = provider.GetRequiredKeyedService<IGrainStorage>(storageName);
        return new Harness(provider, collector, inner, storage);
    }

    private sealed class TestSiloBuilder : ISiloBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
    }

    private sealed record Harness(
        ServiceProvider Provider,
        GrainActivityCollector Collector,
        FakeGrainStorage Inner,
        IGrainStorage Storage) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }
}
