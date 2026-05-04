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
    public void GetStorageOperationsAsync_grain_scope_throws_for_null_grain()
    {
        var collector = new GrainActivityCollector();

        Assert.Throws<ArgumentNullException>(() =>
            collector.GetStorageOperationsAsync<ITestStateGrain>(null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GetGrainCallsAsync_grain_scope_throws_for_null_grain()
    {
        var collector = new GrainActivityCollector();

        Assert.Throws<ArgumentNullException>(() =>
            collector.GetGrainCallsAsync<ITestStateGrain>(null!, cancellationToken: TestContext.Current.CancellationToken));
    }
}
