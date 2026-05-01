using Orleans.Storage;

namespace Egil.Orleans.Testing.Tests;

public class StorageObserverTests
{
    private static readonly Type StorageObserverType = typeof(GrainActivityCollector).Assembly.GetType("Egil.Orleans.Testing.StorageObserver", throwOnError: true)!;

    [Fact]
    public async Task WriteStateAsync_publishes_storage_operation_details()
    {
        var collector = new GrainActivityCollector();
        var inner = new FakeGrainStorage();
        var observer = CreateObserver(inner, collector, "Default");
        var grainId = GrainId.Create("test-grain", "write");
        var state = new TestGrainState<string> { ETag = "etag-1", State = "value" };
        var waitTask = collector.WaitForStorageOperationAsync(
            operation => operation.Kind == StorageOperationKind.Write
                && operation.GrainId == grainId
                && operation.StorageName == "Default"
                && operation.StateName == "state"
                && Equals(operation.State, "value")
                && operation.Etag == "etag-1",
            timeout: TimeSpan.FromMilliseconds(250),
            ct: TestContext.Current.CancellationToken);

        await observer.WriteStateAsync("state", grainId, state);
        await waitTask;

        Assert.Equal(1, inner.WriteCount);
    }

    [Fact]
    public async Task ReadStateAsync_publishes_read_operation()
    {
        var collector = new GrainActivityCollector();
        var inner = new FakeGrainStorage();
        var observer = CreateObserver(inner, collector, "Default");
        var grainId = GrainId.Create("test-grain", "read");
        var state = new TestGrainState<string> { ETag = "etag-2", State = "value" };
        var waitTask = collector.WaitForStorageOperationAsync(
            operation => operation.Kind == StorageOperationKind.Read && operation.GrainId == grainId,
            timeout: TimeSpan.FromMilliseconds(250),
            ct: TestContext.Current.CancellationToken);

        await observer.ReadStateAsync("state", grainId, state);
        await waitTask;

        Assert.Equal(1, inner.ReadCount);
    }

    [Fact]
    public async Task ClearStateAsync_does_not_publish_during_assertion_scope()
    {
        var collector = new GrainActivityCollector();
        var inner = new FakeGrainStorage();
        var observer = CreateObserver(inner, collector, "Default");
        var grainId = GrainId.Create("test-grain", "clear");
        var state = new TestGrainState<string> { ETag = "etag-3", State = "value" };
        var waitTask = collector.WaitForStorageOperationAsync(
            _ => true,
            timeout: TimeSpan.FromMilliseconds(100),
            ct: TestContext.Current.CancellationToken);

        using (RequestContextScope.ForAssertion())
        {
            await observer.ClearStateAsync("state", grainId, state);
        }

        Assert.Equal(1, inner.ClearCount);
        await Assert.ThrowsAsync<WaitForAssertionTimeoutException>(() => waitTask);
    }

    [Fact]
    public void Participate_forwards_to_inner_lifecycle_participant()
    {
        var collector = new GrainActivityCollector();
        var inner = new LifecycleFakeGrainStorage();
        var participant = (ILifecycleParticipant<ISiloLifecycle>)CreateObserver(inner, collector, "Default");

        participant.Participate(null!);

        Assert.Equal(1, inner.ParticipateCount);
    }

    [Fact]
    public void Participate_ignores_non_participant_inner()
    {
        var collector = new GrainActivityCollector();
        var participant = (ILifecycleParticipant<ISiloLifecycle>)CreateObserver(new FakeGrainStorage(), collector, "Default");

        participant.Participate(null!);
    }

    private static IGrainStorage CreateObserver(IGrainStorage inner, GrainActivityCollector collector, string storageName)
        => (IGrainStorage)Activator.CreateInstance(StorageObserverType, inner, collector, storageName)!;

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
}
