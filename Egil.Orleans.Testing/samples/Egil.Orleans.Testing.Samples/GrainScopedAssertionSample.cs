namespace Egil.Orleans.Testing.Samples.GrainScopedAssertions;

// -- Grain definitions -------------------------------------------------------

public interface ICounterGrain : IGrainWithStringKey
{
    Task IncrementAsync();

    Task<int> GetCountAsync();
}

public sealed class CounterState
{
    public int Count { get; set; }
}

public sealed class CounterGrain(
    [PersistentState("counter", "Default")] IPersistentState<CounterState> state)
    : Grain, ICounterGrain
{
    public async Task IncrementAsync()
    {
        state.State.Count++;
        await state.WriteStateAsync();
    }

    public Task<int> GetCountAsync() => Task.FromResult(state.State.Count);
}

// -- Tests -------------------------------------------------------------------

#region grain_scoped_assertions_fixture
/// <summary>
/// Demonstrates grain-scoped <c>WaitForAssertionAsync</c> overloads.
/// Activity from an unrelated grain does not retrigger the assertion.
/// </summary>
public sealed class CounterGrainTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task WaitForAssertionAsync_with_grain_only_retriggers_on_activity_from_that_grain()
    {
        var targetGrain = fixture.GetUniqueGrain<ICounterGrain>();
        var unrelatedGrain = fixture.GetUniqueGrain<ICounterGrain>();

        await targetGrain.IncrementAsync();

        // Pass the grain to the scoped overload.
        // Only activity originating from 'targetGrain' will retrigger this assertion.
        await fixture.WaitForAssertionAsync(targetGrain, async () =>
        {
            Assert.Equal(1, await targetGrain.GetCountAsync());
        }, ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WaitForAssertionAsync_grain_overload_passes_grain_to_lambda()
    {
        var grain = fixture.GetUniqueGrain<ICounterGrain>();

        await grain.IncrementAsync();
        await grain.IncrementAsync();

        // The grain reference is forwarded into the lambda so you can assert
        // without capturing it in a closure.
        var count = await fixture.WaitForAssertionAsync(grain, async (g) =>
        {
            var c = await g.GetCountAsync();
            Assert.True(c >= 2);
            return c;
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, count);
    }
}
#endregion

