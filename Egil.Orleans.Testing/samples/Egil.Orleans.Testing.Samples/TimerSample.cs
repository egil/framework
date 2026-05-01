using Orleans.TestingHost;
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

public sealed class TimerGrainTests(TimerFixture fixture) : IClassFixture<TimerFixture>
{
    #region timer_test
    [Fact]
    public async Task Timer_callback_updates_state()
    {
        var grain = fixture.GrainFactory.GetGrain<ITimerGrain>(Guid.NewGuid().ToString());

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
    #endregion
}

// -- Fixture -----------------------------------------------------------------

public sealed class TimerFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    public GrainActivityCollector Collector { get; } = new();

    public IGrainFactory GrainFactory => cluster!.Client;

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("Default");
            siloBuilder.AddGrainActivityCollector(Collector)
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

    Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => ((IGrainActivityWaiter)Collector).WaitForAssertionAsync(assertion, filter, timeout, cancellationToken);
}
