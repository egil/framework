using System.Collections.Concurrent;
using Orleans.Storage;
using Orleans.TestingHost;

namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateRecoveryActivationTests(StateRecoveryCluster fixture) : IClassFixture<StateRecoveryCluster>
{
    [Theory]
    [InlineData(false, false, "stored")]
    [InlineData(false, true, "candidate")]
    [InlineData(true, false, "stored")]
    [InlineData(true, true, "default")]
    public async Task Fencing_replaces_the_activation_and_reloads_durable_truth_without_a_deactivation_write(
        bool clear, bool persist, string expected)
    {
        var grain = fixture.Cluster.Client.GetGrain<IRecoveryActivationGrain>(Guid.NewGuid());
        var before = await grain.ObserveAsync();
        await grain.SeedAsync();

        var error = await Assert.ThrowsAsync<OrleansException>(() => grain.FailAsync(clear, persist));
        Assert.IsType<IOException>(error.InnerException);
        var evidence = fixture.Storage.For(grain.GetGrainId());
        // The failed call can return before Orleans has removed the fenced activation.
        // Await runtime completion so the next call reaches its replacement.
        await evidence.Deactivated.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var after = await grain.ObserveAsync();

        Assert.NotEqual(before.Activation, after.Activation);
        Assert.Equal(expected, after.Value);
        Assert.Equal("fresh private state", after.PrivateValue);
        Assert.Equal(4, evidence.Reads);
        Assert.Equal(2, evidence.Mutations);
        Assert.True(evidence.DeactivationSaveRejected);
        Assert.True(evidence.HookConfigurationRejected);
    }

    [Fact]
    public async Task Local_readback_override_retains_activation_even_when_the_named_factory_fences()
    {
        var grain = fixture.Cluster.Client.GetGrain<IRecoveryActivationGrain>(Guid.NewGuid());
        var before = await grain.ObserveAsync();

        await grain.UseLocalReadBackAsync();
        var after = await grain.ObserveAsync();

        Assert.Equal(before.Activation, after.Activation);
        Assert.Equal("committed locally", await grain.LocalValueAsync());
        Assert.Equal(3, fixture.Storage.For(grain.GetGrainId()).Reads);
    }
}

public sealed class StateRecoveryCluster : IAsyncLifetime
{
    public RecoveryGrainStorage Storage { get; } = new();
    public InProcessTestCluster Cluster { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddStateManagerFactory("Recovery", typeof(DefaultStateManagerFactory),
                options => options.RecoveryPolicy = StateRecoveryPolicy.FenceAndDeactivate);
            // Deliberately later: injected facets must still inherit the factory override.
            silo.ConfigureStateManager(options => options.RecoveryPolicy = StateRecoveryPolicy.ReadBack);
            silo.Services.AddSingleton(Storage);
            silo.Services.AddKeyedSingleton<IGrainStorage>("Recovery", Storage);
        });
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await Cluster.DisposeAsync();
}

public interface IRecoveryActivationGrain : IGrainWithGuidKey
{
    Task<RecoveryObservation> ObserveAsync();
    Task SeedAsync();
    Task FailAsync(bool clear, bool persist);
    Task UseLocalReadBackAsync();
    Task<string> LocalValueAsync();
}

[GenerateSerializer]
public sealed record RecoveryObservation([property: Id(0)] Guid Activation, [property: Id(1)] string Value,
    [property: Id(2)] string PrivateValue);

[GenerateSerializer]
public sealed record RecoveryActivationState
{
    [Id(0)] public string Value { get; init; } = "default";
}

public sealed class RecoveryActivationGrain : Grain, IRecoveryActivationGrain
{
    private readonly IStateManager<RecoveryActivationState> manager;
    private readonly IPersistentState<RecoveryActivationState> localStorage;
    private readonly RecoveryGrainStorage storage;
    private readonly Guid activation = Guid.NewGuid();
    private string privateValue = "fresh private state";
    private IStateManager<RecoveryActivationState> local = null!;
    private bool failed;

    public RecoveryActivationGrain(
        [PersistentState("injected", "Recovery")] IStateManager<RecoveryActivationState> manager,
        [PersistentState("local", "Recovery")] IPersistentState<RecoveryActivationState> localStorage,
        RecoveryGrainStorage storage)
    {
        this.manager = manager;
        this.localStorage = localStorage;
        this.storage = storage;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        local = await this.RegisterStateManagerAsync("Recovery", localStorage, _ => { },
            configure: options => options.RecoveryPolicy = StateRecoveryPolicy.ReadBack, cancellationToken: cancellationToken);
    }

    public Task<RecoveryObservation> ObserveAsync() => Task.FromResult(new RecoveryObservation(activation, manager.State.Value, privateValue));
    public Task SeedAsync() => manager.WriteAsync(new() { Value = "stored" });
    public Task<string> LocalValueAsync() => Task.FromResult(local.State.Value);

    public Task FailAsync(bool clear, bool persist)
    {
        privateValue = "stale private state";
        manager.State = new() { Value = "must not flush" };
        var evidence = storage.For(this.GetGrainId());
        evidence.Deactivated = GrainContext.Deactivated;
        evidence.FailNext = true;
        evidence.PersistBeforeFailure = persist;
        failed = true;
        return clear ? manager.ClearAsync() : manager.WriteAsync(new() { Value = "candidate" });
    }

    public async Task UseLocalReadBackAsync()
    {
        var evidence = storage.For(this.GetGrainId());
        evidence.FailNext = true;
        evidence.PersistBeforeFailure = true;
        await local.WriteAsync(new() { Value = "committed locally" });
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        if (!failed) return;
        var evidence = storage.For(this.GetGrainId());
        try { await manager.SaveChangesAsync(cancellationToken); }
        catch (InvalidOperationException error) when (error.InnerException is OrleansException { InnerException: IOException })
        {
            evidence.DeactivationSaveRejected = true;
        }
        try { manager.ConfigureHooks(_ => throw new InvalidOperationException("callback must not run")); }
        catch (InvalidOperationException error) when (error.InnerException is OrleansException { InnerException: IOException })
        {
            evidence.HookConfigurationRejected = true;
        }
    }
}

// The provider separates immutable durable records from each activation's facet.
// Failure injection belongs at this boundary: Orleans still owns hydration,
// directory routing, deactivation, and the replacement grain instance.
public sealed class RecoveryGrainStorage : IGrainStorage
{
    private readonly ConcurrentDictionary<(GrainId, string), object> records = new();
    private readonly ConcurrentDictionary<GrainId, RecoveryEvidence> evidence = new();
    public RecoveryEvidence For(GrainId id) => evidence.GetOrAdd(id, _ => new());

    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        For(grainId).Reads++;
        grainState.RecordExists = records.TryGetValue((grainId, stateName), out var value);
        grainState.State = grainState.RecordExists ? (T)value! : Activator.CreateInstance<T>();
        return Task.CompletedTask;
    }

    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        => Mutate(stateName, grainId, grainState, clear: false);

    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        => Mutate(stateName, grainId, grainState, clear: true);

    private Task Mutate<T>(string stateName, GrainId grainId, IGrainState<T> grainState, bool clear)
    {
        var activity = For(grainId);
        activity.Mutations++;
        var fail = activity.FailNext;
        activity.FailNext = false;
        if (!fail || activity.PersistBeforeFailure)
        {
            if (clear) records.TryRemove((grainId, stateName), out _);
            else records[(grainId, stateName)] = grainState.State!;
            grainState.RecordExists = !clear;
        }
        if (fail) throw new IOException("Injected lost storage response.");
        return Task.CompletedTask;
    }
}

public sealed class RecoveryEvidence
{
    public Task Deactivated { get; set; } = null!;
    public bool FailNext { get; set; }
    public bool PersistBeforeFailure { get; set; }
    public bool DeactivationSaveRejected { get; set; }
    public bool HookConfigurationRejected { get; set; }
    public int Reads { get; set; }
    public int Mutations { get; set; }
}
