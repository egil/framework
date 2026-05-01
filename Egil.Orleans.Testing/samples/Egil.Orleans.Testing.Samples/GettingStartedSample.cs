using Orleans.TestingHost;
using Orleans.Timers;

namespace Egil.Orleans.Testing.Samples.GettingStarted;

#region readme_inline_setup
// -- Grain interface & state -------------------------------------------------

public interface IOrderGrain : IGrainWithStringKey
{
    Task SubmitAsync(string item);

    Task<string?> GetLastSubmittedItemAsync();
}

public sealed class OrderState
{
    public string? PendingItem { get; set; }

    public string? LastSubmittedItem { get; set; }
}

// -- Grain implementation ----------------------------------------------------

public sealed class OrderGrain(
    [PersistentState("order", "Default")] IPersistentState<OrderState> state,
    ITimerRegistry timerRegistry,
    IGrainContext grainContext)
    : Grain, IOrderGrain
{
    private IGrainTimer? timer;

    public async Task SubmitAsync(string item)
    {
        // This extra timer hop is intentionally a little indirect.
        // It exists here to demonstrate the kind of async follow-up work
        // that is awkward to test reliably with a plain immediate assertion.
        state.State.PendingItem = item;
        await state.WriteStateAsync();

        timer?.Dispose();
        timer = timerRegistry.RegisterGrainTimer(
            grainContext,
            static (grain, ct) => grain.OnSubmissionCompletedAsync(ct),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMilliseconds(1),
                Period = Timeout.InfiniteTimeSpan,
            });
    }

    public Task<string?> GetLastSubmittedItemAsync()
        => Task.FromResult(state.State.LastSubmittedItem);

    private async Task OnSubmissionCompletedAsync(CancellationToken cancellationToken)
    {
        state.State.LastSubmittedItem = state.State.PendingItem;
        await state.WriteStateAsync();
        timer?.Dispose();
        timer = null;
    }
}

// -- Tests -------------------------------------------------------------------

/// <summary>
/// Example: build an <see cref="InProcessTestCluster"/> directly in the test arrange step.
/// </summary>
public sealed class OrderGrainInlineSetupTests
{
    [Fact]
    public async Task SubmitAsync_sets_last_submitted_item()
    {
        var collector = new GrainActivityCollector();
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("Default");
            siloBuilder.AddGrainActivityCollector(collector)
                .CollectStorageActivityFromDefault();
        });

        await using var cluster = builder.Build();
        await cluster.DeployAsync();

        var grain = cluster.Client.GetGrain<IOrderGrain>(Guid.NewGuid().ToString("N"));

        await grain.SubmitAsync("laptop");

        // SubmitAsync only schedules the follow-up work on a grain timer.
        // The method returns before the timer callback writes the final state,
        // so a direct assertion here would race and tempt you to add Task.Delay.
        // WaitForAssertionAsync retries whenever the collector observes the
        // timer callback's storage write, which makes the test deterministic.
        await collector.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("laptop", await grain.GetLastSubmittedItemAsync());
        }, ct: TestContext.Current.CancellationToken);
    }
}
#endregion

#region readme_fixture_usage
public sealed class OrderGrainFixtureTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task SubmitAsync_sets_last_submitted_item()
    {
        var grain = fixture.GetUniqueGrain<IOrderGrain>();
        await grain.SubmitAsync("monitor");

        await fixture.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("monitor", await grain.GetLastSubmittedItemAsync());
        }, ct: TestContext.Current.CancellationToken);
    }
}
#endregion
