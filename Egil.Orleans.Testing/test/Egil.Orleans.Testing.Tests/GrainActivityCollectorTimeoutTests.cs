using System.Runtime.CompilerServices;

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

    [Fact]
    public async Task WaitForAssertionAsync_timeout_exception_stack_trace_starts_at_assertion_failure()
    {
        var collector = new GrainActivityCollector();
        var exception = await Assert.ThrowsAsync<WaitForAssertionTimeoutException>(() =>
            collector.WaitForAssertionAsync(
                ThrowFailedAssertionAsync,
                timeout: TimeSpan.FromMilliseconds(100),
                ct: TestContext.Current.CancellationToken));

        var frames = exception.StackTrace?
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.TrimStart().StartsWith("at ", StringComparison.Ordinal))
            .ToArray();

        Assert.NotNull(frames);
        var firstFrame = frames!.First();
        var firstUserFrame = frames!.FirstOrDefault(static line => line.Contains("Egil.Orleans.Testing.Tests", StringComparison.Ordinal));

        Assert.Contains("Assert.Equal", firstFrame, StringComparison.Ordinal);
        Assert.NotNull(firstUserFrame);
        Assert.Contains(nameof(ThrowFailedAssertionAsync), firstUserFrame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForAssertionAsync_throws_operation_canceled_when_token_is_already_canceled()
    {
        var collector = new GrainActivityCollector();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            collector.WaitForAssertionAsync(
                static () => Task.FromException(new Xunit.Sdk.XunitException("The first attempt must fail so the wait enters the activity loop.")),
                timeout: TimeSpan.FromMilliseconds(250),
                ct: cts.Token));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task ThrowFailedAssertionAsync()
    {
        Assert.Equal("expected", "actual");
        return Task.CompletedTask;
    }
}
