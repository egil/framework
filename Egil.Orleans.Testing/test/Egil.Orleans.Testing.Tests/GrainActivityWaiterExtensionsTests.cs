namespace Egil.Orleans.Testing.Tests;

public class GrainActivityWaiterExtensionsTests
{
    [Fact]
    public async Task WaitForAssertionAsync_forwards_to_collector()
    {
        IGrainActivityWaiter waiter = new ForwardingWaiter(new GrainActivityCollector());

        await waiter.WaitForAssertionAsync(
            static () => Task.CompletedTask,
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WaitForAssertionAsync_throws_for_null_waiter()
    {
        IGrainActivityWaiter waiter = null!;

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            waiter.WaitForAssertionAsync(
                static () => Task.CompletedTask,
                ct: TestContext.Current.CancellationToken));

        Assert.Equal("waiter", exception.ParamName);
    }

    private sealed class ForwardingWaiter(GrainActivityCollector collector) : IGrainActivityWaiter
    {
        Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
            Func<ValueTask<TResult>> assertion,
            Predicate<GrainActivity>? filter,
            TimeSpan? timeout,
            CancellationToken ct)
            => ((IGrainActivityWaiter)collector).WaitForAssertionAsync(assertion, filter, timeout, ct);
    }
}
