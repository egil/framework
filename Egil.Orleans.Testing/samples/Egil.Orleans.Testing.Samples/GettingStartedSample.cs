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
/// Example: build an <see cref="InProcessTestCluster"/> directly in the test arrange step.
/// </summary>
public sealed class OrderGrainTests
{
    [Fact]
    public async Task SubmitAsync_sets_status_to_submitted()
    {
        var collector = new GrainActivityCollector();
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorageAsDefault();
            siloBuilder.AddMemoryGrainStorage("Orders");

            siloBuilder.AddGrainActivityCollector(collector)
                .CollectStorageActivityFromDefault()
                .CollectStorageActivityFrom("Orders");
        });

        await using var cluster = builder.Build();
        await cluster.DeployAsync();

        var grain = cluster.Client.GetGrain<IOrderGrain>(Guid.NewGuid().ToString());

        await grain.SubmitAsync("laptop");

        await collector.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("submitted", await grain.GetStatusAsync());
        }, ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubmitAsync_stores_item()
    {
        var collector = new GrainActivityCollector();
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorageAsDefault();
            siloBuilder.AddGrainActivityCollector(collector)
                .CollectStorageActivityFromDefault();
        });

        await using var cluster = builder.Build();
        await cluster.DeployAsync();

        var grain = cluster.Client.GetGrain<IOrderGrain>(Guid.NewGuid().ToString());

        await grain.SubmitAsync("keyboard");

        await collector.WaitForAssertionAsync(grain, async (g) =>
        {
            var status = await g.GetStatusAsync();
            Assert.NotNull(status);
            Assert.Equal("submitted", status);
        }, ct: TestContext.Current.CancellationToken);
    }
}
#endregion
