namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorCoreTests
{
    [Fact]
    public async Task WaitForAssertionAsync_with_grain_parameter_task_throws_for_null_grain()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForAssertionAsync<ITestStateGrain>(
                null!,
                static _ => Task.CompletedTask,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_grain_parameter_task_result_throws_for_null_grain()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForAssertionAsync<ITestStateGrain, int>(
                null!,
                static _ => Task.FromResult(1),
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForStorageOperationAsync_with_grain_scope_throws_for_null_grain()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForStorageOperationAsync<ITestStateGrain>(
                null!,
                static _ => true,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForGrainCallAsync_with_grain_scope_throws_for_null_grain()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForGrainCallAsync<ITestStateGrain>(
                null!,
                static _ => true,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForStorageOperationAsync_throws_for_null_predicate()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForStorageOperationAsync(
                (Func<StorageOperation, bool>)null!,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForGrainCallAsync_throws_for_null_predicate()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForGrainCallAsync(
                (Func<IIncomingGrainCallContext, bool>)null!,
                ct: TestContext.Current.CancellationToken));
    }
}
