namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class PausedJournalWriteTests
{
    [Fact]
    public async Task Cleanup_releases_the_write_without_masking_the_original_failure()
    {
        var gate = new PausedJournalWrite();
        var pending = FailAfterReleaseAsync(gate);
        gate.Observe(pending);
        var original = new InvalidOperationException("Original test failure.");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using (gate)
            {
                throw original;
            }
        });

        Assert.Same(original, actual);
        await Assert.ThrowsAsync<IOException>(() => pending);
    }

    private static async Task FailAfterReleaseAsync(PausedJournalWrite gate)
    {
        await gate.WaitAsync(TestContext.Current.CancellationToken);
        throw new IOException("Write failed after release.");
    }
}
