using Orleans.Timers;

namespace Egil.Orleans.Testing.Samples.Timers;

// -- Grain definitions -------------------------------------------------------

public interface ITimerGrain : IGrainWithStringKey
{
    Task StartAsync(string value);

    Task<string?> GetLastValueAsync();
}

public sealed class TimerGrainState
{
    public string? PendingValue { get; set; }

    public string? LastValue { get; set; }
}

#region timer_grain_implementation
public sealed class TimerGrain(
    [PersistentState("state", "Default")] IPersistentState<TimerGrainState> state,
    ITimerRegistry timerRegistry,
    IGrainContext grainContext)
    : Grain, ITimerGrain
{
    private IGrainTimer? timer;

    public async Task StartAsync(string value)
    {
        state.State.PendingValue = value;
        await state.WriteStateAsync();

        timer?.Dispose();
        timer = timerRegistry.RegisterGrainTimer(
            grainContext,
            static (grain, ct) => grain.OnTimerTickAsync(ct),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMilliseconds(1),
                Period = Timeout.InfiniteTimeSpan,
            });
    }

    public Task<string?> GetLastValueAsync() => Task.FromResult(state.State.LastValue);

    private async Task OnTimerTickAsync(CancellationToken cancellationToken)
    {
        state.State.LastValue = state.State.PendingValue;
        await state.WriteStateAsync();
        timer?.Dispose();
        timer = null;
    }
}
#endregion

// -- Tests -------------------------------------------------------------------

#region timer_test
public sealed class TimerGrainTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task Timer_callback_updates_state()
    {
        var grain = fixture.GetUniqueGrain<ITimerGrain>();

        // Act — trigger the grain timer.
        await grain.StartAsync("timer-value");

        // Assert — the timer callback fires asynchronously; the collector retries
        // the assertion each time grain activity (the storage write inside the
        // timer callback) is observed.
        await fixture.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("timer-value", await grain.GetLastValueAsync());
        }, ct: TestContext.Current.CancellationToken);
    }
}
#endregion

