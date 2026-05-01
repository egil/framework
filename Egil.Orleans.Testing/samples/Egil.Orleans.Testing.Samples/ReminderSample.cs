using Orleans.TestingHost;
using Orleans.Timers;
using System.Runtime.CompilerServices;

namespace Egil.Orleans.Testing.Samples.Reminders;

// -- Grain definitions -------------------------------------------------------

public interface IReminderGrain : IGrainWithStringKey
{
    Task ScheduleAsync(string value);

    Task<string?> GetLastValueAsync();
}

public sealed class ReminderGrainState
{
    public string? PendingValue { get; set; }

    public string? LastValue { get; set; }
}

#region reminder_grain_implementation
public sealed class ReminderGrain(
    [PersistentState("state", "Default")] IPersistentState<ReminderGrainState> state,
    IReminderRegistry reminderRegistry,
    IGrainContext grainContext)
    : Grain, IReminderGrain, IRemindable
{
    private const string ReminderName = "process-reminder";
    private IGrainReminder? reminder;

    public async Task ScheduleAsync(string value)
    {
        state.State.PendingValue = value;
        await state.WriteStateAsync();
        reminder = await reminderRegistry.RegisterOrUpdateReminder(
            grainContext.GrainId,
            ReminderName,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(5));
    }

    public Task<string?> GetLastValueAsync() => Task.FromResult(state.State.LastValue);

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        state.State.LastValue = state.State.PendingValue;
        await state.WriteStateAsync();

        if (reminder is not null)
        {
            await reminderRegistry.UnregisterReminder(grainContext.GrainId, reminder);
            reminder = null;
        }
    }
}
#endregion

// -- Tests -------------------------------------------------------------------

#region reminder_test
public sealed class ReminderGrainTests(ReminderFixture fixture) : IClassFixture<ReminderFixture>
{
    [Fact]
    public async Task Reminder_callback_updates_state()
    {
        var grain = fixture.GetUniqueGrain<IReminderGrain>();

        // Arrange — register a reminder that fires after 1 minute.
        await grain.ScheduleAsync("reminder-value");

        // Advance the deterministic clock past the reminder due time.
        await fixture.ReminderClock.AdvanceAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        // Assert after triggering the callback. WaitForAssertionAsync retries until the reminder work is visible.
        await fixture.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("reminder-value", await grain.GetLastValueAsync());
        }, ct: TestContext.Current.CancellationToken);
    }
}
#endregion

// -- Fixture -----------------------------------------------------------------

#region orleans_test_cluster_fixture
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
///
/// The protected hook methods make it practical for a derived fixture to keep the common setup
/// while adding a small amount of sample-specific configuration. That is useful for reminders,
/// streams, or other Orleans features that need extra silo registration on top of the baseline setup.
/// </remarks>
public class OrleansTestClusterFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    // The collector observes grain calls and, by default, storage writes inside the silo.
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

    /// <summary>
    /// Controls whether the base fixture should observe writes to the <c>Default</c> grain storage provider.
    /// </summary>
    /// <remarks>
    /// Most sample fixtures should leave this enabled because storage writes are a strong signal for
    /// <see cref="IGrainActivityWaiter.WaitForAssertionAsync{TResult}"/>. Derived fixtures can turn it off
    /// when grain-call observation alone is sufficient.
    /// </remarks>
    protected virtual bool CollectStorageActivityFromDefault => true;

    public async ValueTask InitializeAsync()
    {
        // Build a one-silo in-process test cluster. Most samples only need one silo,
        // which keeps startup cost and overall test complexity low.
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        // Let derived fixtures register cluster-wide concerns before the common silo setup runs.
        // ReminderFixture uses this to attach a deterministic TimeProvider.
        ConfigureClusterBuilder(builder);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            // Register a default storage provider so grains using [PersistentState(..., "Default")]
            // can read and write state without any external infrastructure.
            siloBuilder.AddMemoryGrainStorage("Default");

            // AddGrainActivityCollector wires up grain call observation automatically.
            // Derived fixtures can opt out of the default storage observer if they only need call signals.
            var activityCollectorBuilder = siloBuilder.AddGrainActivityCollector(Collector);
            if (CollectStorageActivityFromDefault)
            {
                activityCollectorBuilder.CollectStorageActivityFromDefault();
            }

            // Let derived fixtures add their own silo services after the baseline test setup is in place.
            ConfigureSiloBuilder(siloBuilder);
        });

        // Build first, then deploy. DeployAsync starts the silo and makes the client available.
        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Give derived fixtures a chance to clean up resources that should go away
        // before the cluster itself is torn down. ReminderFixture uses this for its manual clock.
        await DisposeAsyncCore();

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

    /// <summary>
    /// Allows a derived fixture to customize the <see cref="InProcessTestClusterBuilder"/>
    /// before the shared silo configuration is applied.
    /// </summary>
    protected virtual void ConfigureClusterBuilder(InProcessTestClusterBuilder builder)
    {
    }

    /// <summary>
    /// Allows a derived fixture to append feature-specific registrations to the silo.
    /// </summary>
    protected virtual void ConfigureSiloBuilder(ISiloBuilder siloBuilder)
    {
    }

    /// <summary>
    /// Allows a derived fixture to dispose reminder clocks, streams, or other resources
    /// before the cluster itself is shut down.
    /// </summary>
    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;

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
#endregion

#region reminder_fixture
/// <summary>
/// Reusable Orleans reminder test fixture.
/// Copy this into your own test project when reminder-driven tests need deterministic time.
/// </summary>
/// <remarks>
/// This fixture extends <see cref="OrleansTestClusterFixture"/> with the one extra capability
/// reminder tests need: a dedicated <see cref="ReminderTestClock"/> that tests can advance explicitly.
/// Keep this fixture separate from general-purpose fixtures because the manual clock stops normal
/// time progression inside the cluster.
/// </remarks>
public sealed class ReminderFixture : OrleansTestClusterFixture
{
    // This manual clock is the key reminder-specific feature.
    // Tests advance it explicitly to trigger reminder callbacks without waiting on wall-clock time.
    public ReminderTestClock ReminderClock { get; } = new();

    // Reminder callbacks arrive as grain calls, so this fixture can rely on call observation alone.
    // That keeps the sample focused on the reminder-specific behavior instead of storage monitoring.
    protected override bool CollectStorageActivityFromDefault => false;

    protected override void ConfigureClusterBuilder(InProcessTestClusterBuilder builder)
    {
        // Attach the deterministic clock before configuring the silo so Orleans reminder infrastructure
        // uses the manual time provider from the start.
        ReminderTestClock.Attach(builder, ReminderClock);
    }

    protected override void ConfigureSiloBuilder(ISiloBuilder siloBuilder)
    {
        // Enable the in-memory reminder service for the test cluster.
        siloBuilder.UseInMemoryReminderService();
    }

    protected override ValueTask DisposeAsyncCore()
    {
        // Dispose the manual clock first so any reminder-specific resources are cleaned up promptly.
        ReminderClock.Dispose();
        return ValueTask.CompletedTask;
    }
}
#endregion
