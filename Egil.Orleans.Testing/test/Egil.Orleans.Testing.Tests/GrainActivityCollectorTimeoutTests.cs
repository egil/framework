namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorTimeoutTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_throws_timeout_exception_with_last_failure()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var exception = await Assert.ThrowsAsync<WaitForAssertionTimeoutException>(() =>
            fixture.Collector.WaitForAssertionAsync(
                async () => Assert.Equal("expected", await grain.GetValueAsync()),
                timeout: TimeSpan.FromMilliseconds(150),
                ct: TestContext.Current.CancellationToken));

        Assert.NotNull(exception.InnerException);
    }
}
