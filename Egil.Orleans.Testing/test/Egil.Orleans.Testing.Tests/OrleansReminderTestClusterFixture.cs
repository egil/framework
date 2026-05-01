using Orleans.TestingHost;

namespace Egil.Orleans.Testing.Tests;

/// <summary>
/// Dedicated cluster fixture for reminder tests that need deterministic time.
/// </summary>
public sealed class OrleansReminderTestClusterFixture : OrleansTestClusterFixture
{
    public ReminderTestClock ReminderClock { get; } = new();

    /// <inheritdoc />
    protected override void ConfigureClusterBuilder(InProcessTestClusterBuilder builder)
    {
        ReminderTestClock.Attach(builder, ReminderClock);
    }

    /// <inheritdoc />
    protected override ValueTask DisposeAsyncCore()
    {
        ReminderClock.Dispose();
        return ValueTask.CompletedTask;
    }
}
