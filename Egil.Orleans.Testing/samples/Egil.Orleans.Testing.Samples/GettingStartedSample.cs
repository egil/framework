using Orleans.TestingHost;

namespace Egil.Orleans.Testing.Samples.GettingStarted;

// -- Grain interface & state -------------------------------------------------

public interface IOrderGrain : IGrainWithStringKey
{
    Task SubmitAsync(string item);

    Task<string?> GetStatusAsync();
}

public sealed class OrderState
{
    public string? Item { get; set; }

    public string? Status { get; set; }
}

// -- Grain implementation ----------------------------------------------------

public sealed class OrderGrain(
    [PersistentState("order", "Default")] IPersistentState<OrderState> state)
    : Grain, IOrderGrain
{
    public async Task SubmitAsync(string item)
    {
        state.State.Item = item;
        state.State.Status = "submitted";
        await state.WriteStateAsync();
    }

    public Task<string?> GetStatusAsync() => Task.FromResult(state.State.Status);
}

// -- Tests -------------------------------------------------------------------

#region getting_started_test_class
/// <summary>
/// Example: inline <see cref="InProcessTestCluster"/> in a test class,
/// showing both storage observation and grain call observation.
/// </summary>
public sealed class OrderGrainTests : IAsyncLifetime
{
    private InProcessTestCluster? cluster;

    // A single collector shared across all tests in this class.
    private readonly GrainActivityCollector collector = new();

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            // Required: in-memory storage for grain state.
            siloBuilder.AddMemoryGrainStorage("Default");

            // Enable the activity collector.
            // AddGrainActivityCollector wires up grain call observation automatically.
            // CollectStorageActivityFromDefault also enables storage observation.
            siloBuilder.AddGrainActivityCollector(collector)
                .CollectStorageActivityFromDefault();
        });

        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (cluster is not null)
        {
            await cluster.DisposeAsync();
        }
    }

    [Fact]
    public async Task SubmitAsync_sets_status_to_submitted()
    {
        var grain = cluster!.Client.GetGrain<IOrderGrain>(Guid.NewGuid().ToString());

        // Trigger grain activity.
        await grain.SubmitAsync("laptop");

        // WaitForAssertionAsync retries the assertion each time grain activity
        // is detected (storage write or grain call), so the assertion runs
        // immediately, and again each time grain activity fires.
        await collector.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("submitted", await grain.GetStatusAsync());
        }, ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubmitAsync_stores_item()
    {
        var grain = cluster!.Client.GetGrain<IOrderGrain>(Guid.NewGuid().ToString());

        await grain.SubmitAsync("keyboard");

        // Grain-scoped variant: only activity from this specific grain retriggers the assertion.
        await collector.WaitForAssertionAsync(grain, async (g) =>
        {
            var status = await g.GetStatusAsync();
            Assert.NotNull(status);
            Assert.Equal("submitted", status);
        }, ct: TestContext.Current.CancellationToken);
    }
}
#endregion
