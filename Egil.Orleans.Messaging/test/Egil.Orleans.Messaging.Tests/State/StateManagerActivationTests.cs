using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerActivationTests(MessagingTestClusterFixture fixture)
    : IClassFixture<MessagingTestClusterFixture>
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Constructor_registered_manager_forwards_cancellation(int operation)
    {
        var grain = fixture.GrainFactory.GetGrain<IStateManagerActivationGrain>(Guid.NewGuid());

        Assert.True(await grain.CancelOperationAsync(operation));

        var state = await grain.InspectAsync();
        Assert.Equal("default", state.Value);
        Assert.False(state.RecordExists);
    }

    [Fact]
    public async Task Runtime_clock_configuration_reaches_the_tracker_replaced_by_a_read()
    {
        var grain = fixture.GrainFactory.GetGrain<IStateManagerActivationGrain>(Guid.NewGuid());
        await grain.SaveAndDeactivateAsync("saved");

        Assert.True(await grain.UsesConfiguredClockAfterReadAsync());
    }

    [Fact]
    public async Task Constructor_registration_defers_defaults_until_state_is_loaded()
    {
        var grain = fixture.GrainFactory.GetGrain<IStateManagerActivationGrain>(Guid.NewGuid());

        var state = await grain.InspectAsync();

        Assert.True(state.ConstructorReadRejected);
        Assert.Equal("default", state.Value);
        Assert.Equal("default", state.ActivationValue);
        Assert.Equal(1, state.FactoryCalls);
        Assert.Equal(1, state.ConfigureCalls);
        Assert.False(state.RecordExists);
    }

    [Fact]
    public async Task Constructor_registered_manager_uses_persisted_state_on_reactivation()
    {
        var grain = fixture.GrainFactory.GetGrain<IStateManagerActivationGrain>(Guid.NewGuid());
        await grain.SaveAndDeactivateAsync("saved");

        var state = await grain.InspectAsync();

        Assert.Equal("saved", state.Value);
        Assert.Equal("saved", state.ActivationValue);
        Assert.Equal(0, state.FactoryCalls);
        Assert.Equal(1, state.ConfigureCalls);
        Assert.True(state.RecordExists);
    }
}

public interface IStateManagerActivationGrain : IGrainWithGuidKey
{
    Task<bool> CancelOperationAsync(int operation);
    Task<ActivationStateObservation> InspectAsync();
    Task SaveAndDeactivateAsync(string value);
    Task<bool> UsesConfiguredClockAfterReadAsync();
}

[GenerateSerializer]
public sealed record ActivationStateObservation(
    [property: Id(0)] string Value,
    [property: Id(1)] string ActivationValue,
    [property: Id(2)] bool RecordExists,
    [property: Id(3)] int FactoryCalls,
    [property: Id(4)] int ConfigureCalls,
    [property: Id(5)] bool ConstructorReadRejected);

[GenerateSerializer]
public sealed record ActivationManagedState
{
    [Id(0)] public string Value { get; init; } = "provider-default";
    [Id(1)] public MessageTracker Tracker { get; init; } = new();
}

public sealed class StateManagerActivationGrain : Grain, IStateManagerActivationGrain
{
    private readonly IPersistentState<ActivationManagedState> storage;
    private readonly IStateManager<ActivationManagedState> manager;
    private readonly bool constructorReadRejected;
    private readonly ManualTimeProvider time;
    private int factoryCalls;
    private int configureCalls;
    private string activationValue = "not activated";

    public StateManagerActivationGrain(
        [PersistentState("state", "Default")] IPersistentState<ActivationManagedState> storage,
        ManualTimeProvider time)
    {
        this.time = time;
        this.storage = storage;
        manager = this.RegisterStateManager("Default", storage,
            () => { factoryCalls++; return new() { Value = "default" }; },
            state => { configureCalls++; state.Tracker.RegisterTimeProvider(time); });
        try
        {
            _ = manager.State;
        }
        catch (InvalidOperationException)
        {
            constructorReadRejected = true;
        }
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        activationValue = manager.State.Value;
        return Task.CompletedTask;
    }

    public Task<ActivationStateObservation> InspectAsync() => Task.FromResult(new ActivationStateObservation(
        manager.State.Value, activationValue, storage.RecordExists,
        factoryCalls, configureCalls, constructorReadRejected));

    public async Task<bool> UsesConfiguredClockAfterReadAsync()
    {
        await manager.ReadAsync();
        var now = time.GetUtcNow();
        var sender = GrainContext.GrainId;
        manager.State.Tracker.TryAcceptMessage(new OutboxSequenceToken(1, sender, now, now), out var tracked);
        return tracked.Evict(sender, now.AddSeconds(1)).LatestOutbox(sender) is null;
    }

    public async Task<bool> CancelOperationAsync(int operation)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        try
        {
            await (operation switch
            {
                0 => manager.ReadAsync(cancellation.Token),
                1 => manager.WriteAsync(manager.State with { Value = "canceled" }, cancellation.Token),
                2 => manager.ClearAsync(cancellation.Token),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            });
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    public async Task SaveAndDeactivateAsync(string value)
    {
        await manager.WriteAsync(manager.State with { Value = value });
        DeactivateOnIdle();
    }
}
