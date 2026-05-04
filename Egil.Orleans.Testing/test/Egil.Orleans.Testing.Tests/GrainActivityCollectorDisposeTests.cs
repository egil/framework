namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorDisposeTests
{
    [Fact]
    public void Dispose_is_idempotent()
    {
        var collector = new GrainActivityCollector();
        collector.Dispose();
        collector.Dispose(); // should not throw
    }

    [Fact]
    public async Task WaitForAssertionAsync_throws_ObjectDisposedException_after_dispose()
    {
        var collector = new GrainActivityCollector();
        collector.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            collector.WaitForAssertionAsync(
                static () => Task.CompletedTask,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForStorageOperationAsync_throws_ObjectDisposedException_after_dispose()
    {
        var collector = new GrainActivityCollector();
        collector.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            collector.WaitForStorageOperationAsync(
                static _ => true,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForGrainCallAsync_throws_ObjectDisposedException_after_dispose()
    {
        var collector = new GrainActivityCollector();
        collector.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            collector.WaitForGrainCallAsync(
                static _ => true,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SubscribeToStorageOperations_throws_ObjectDisposedException_after_dispose()
    {
        var collector = new GrainActivityCollector();
        collector.Dispose();

        var enumerable = collector.SubscribeToStorageOperations(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in enumerable)
            {
            }
        });
    }

    [Fact]
    public async Task SubscribeToGrainCalls_throws_ObjectDisposedException_after_dispose()
    {
        var collector = new GrainActivityCollector();
        collector.Dispose();

        var enumerable = collector.SubscribeToGrainCalls(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in enumerable)
            {
            }
        });
    }

    [Fact]
    public void OnStorageOperation_is_silent_after_dispose()
    {
        var collector = new GrainActivityCollector();
        collector.Dispose();

        // Should not throw — publish is a no-op after dispose.
        var operation = new StorageOperation(
            kind: StorageOperationKind.Write,
            grainId: GrainId.Create("test", "key"),
            storageName: "Default",
            stateName: "state",
            etag: null,
            state: null);

        collector.OnStorageOperation(operation);
    }
}
