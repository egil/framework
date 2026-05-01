using Orleans.TestingHost;
using Orleans.Timers;
using System.Runtime.CompilerServices;

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

// -- Fixture -----------------------------------------------------------------

/// <summary>
/// Minimal reusable Orleans test cluster fixture.
/// Copy this into your own test project when several tests need the same cluster setup.
/// </summary>
/// <remarks>
/// The fixture combines three responsibilities:
/// 1. Own the lifecycle of an <see cref="InProcessTestCluster"/>.
/// 2. Expose a ready-to-use <see cref="IGrainFactory"/> for test code.
/// 3. Forward <see cref="IGrainActivityWaiter"/> calls to a <see cref="GrainActivityCollector"/>
///    so tests can call <c>fixture.WaitForAssertionAsync(...)</c> directly.
/// </remarks>
public sealed class OrleansTestClusterFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    // The collector observes grain calls and storage writes inside the silo.
    // WaitForAssertionAsync uses those activity signals to know when to retry assertions.
    public GrainActivityCollector Collector { get; } = new();

    // Expose the client grain factory so tests do not need to reach into the cluster directly.
    public IGrainFactory GrainFactory => cluster!.Client;

    /// <summary>
    /// Creates a unique <see cref="GrainId"/> for the current test method.
    /// </summary>
    /// <remarks>
    /// This is useful when a test needs a stable identifier that must be shared between
    /// a grain reference and some other Orleans concept such as a stream id or reminder name.
    /// The generated key includes the calling test method name and grain interface name,
    /// which makes copied snippets easier to reason about while still avoiding collisions.
    /// </remarks>
    public GrainId CreateUniqueGrainId<TGrain>([CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName).GetGrainId();

    /// <summary>
    /// Gets a grain reference with a test-unique key.
    /// </summary>
    /// <remarks>
    /// Prefer this helper over hard-coded keys in sample-style tests.
    /// It keeps parallel tests isolated from each other and removes boilerplate
    /// around choosing the correct Orleans key type for the grain interface.
    /// </remarks>
    public TGrain GetUniqueGrain<TGrain>([CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName);

    public async ValueTask InitializeAsync()
    {
        // Build a one-silo in-process test cluster. Most samples only need one silo,
        // which keeps startup cost and overall test complexity low.
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            // Register a default storage provider so grains using [PersistentState(..., "Default")]
            // can read and write state without any external infrastructure.
            siloBuilder.AddMemoryGrainStorage("Default");

            // AddGrainActivityCollector wires up grain call observation automatically.
            // CollectStorageActivityFromDefault also enables storage observation for the
            // "Default" provider, which lets WaitForAssertionAsync react to persisted state changes.
            siloBuilder.AddGrainActivityCollector(Collector)
                .CollectStorageActivityFromDefault();
        });

        // Build first, then deploy. DeployAsync starts the silo and makes the client available.
        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Always tear the cluster down after the test run so ports, timers, and other resources
        // are not kept alive across unrelated tests.
        if (cluster is not null)
        {
            await cluster.DisposeAsync();
        }
    }

    // Forward the waiting API through the fixture so tests can stay focused on intent:
    //   await fixture.WaitForAssertionAsync(...)
    // instead of:
    //   await fixture.Collector.WaitForAssertionAsync(...)
    Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => ((IGrainActivityWaiter)Collector).WaitForAssertionAsync(assertion, filter, timeout, cancellationToken);

    private TGrain CreateUniqueGrainReference<TGrain>(string memberName)
        where TGrain : IGrain
    {
        var grainType = typeof(TGrain);
        var grainName = grainType.Name;

        // Match Orleans key-shape conventions based on the grain interface marker.
        // This lets the same helper work for string, Guid, integer, and compound-key grains.
        return typeof(IGrainWithStringKey).IsAssignableFrom(grainType)
            ? (TGrain)GrainFactory.GetGrain(grainType, $"{memberName}-{grainName}-{Guid.NewGuid():N}")
            : typeof(IGrainWithGuidCompoundKey).IsAssignableFrom(grainType)
                ? (TGrain)GrainFactory.GetGrain(grainType, Guid.NewGuid(), $"{memberName}-{grainName}")
                : typeof(IGrainWithGuidKey).IsAssignableFrom(grainType)
                    ? (TGrain)GrainFactory.GetGrain(grainType, Guid.NewGuid())
                    : typeof(IGrainWithIntegerCompoundKey).IsAssignableFrom(grainType)
                        ? (TGrain)GrainFactory.GetGrain(grainType, Random.Shared.NextInt64(1, long.MaxValue), $"{memberName}-{grainName}")
                        : typeof(IGrainWithIntegerKey).IsAssignableFrom(grainType)
                            ? (TGrain)GrainFactory.GetGrain(grainType, Random.Shared.NextInt64(1, long.MaxValue))
                            : throw new NotSupportedException($"Unsupported grain key type for {grainType.FullName}.");
    }
}
