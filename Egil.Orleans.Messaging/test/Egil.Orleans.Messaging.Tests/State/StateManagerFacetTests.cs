using Orleans.TestingHost;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerFacetTests(StateManagerFacetFixture fixture)
    : IClassFixture<StateManagerFacetFixture>
{
    [Fact]
    public async Task Injected_manager_is_hydrated_before_activation_and_round_trips()
    {
        var grain = fixture.GrainFactory.GetGrain<IFacetGrain>(Guid.NewGuid());

        var initial = await grain.InspectAsync();
        Assert.Equal("facet-default", initial.Value);
        Assert.Equal("facet-default", initial.ActivationValue);

        await grain.SaveAndDeactivateAsync("saved");

        var reloaded = await grain.InspectAsync();
        Assert.Equal("saved", reloaded.Value);
        Assert.Equal("saved", reloaded.ActivationValue);
    }

    [Fact]
    public async Task Injected_manager_defers_state_access_past_the_constructor()
    {
        var grain = fixture.GrainFactory.GetGrain<IFacetGrain>(Guid.NewGuid());

        var observation = await grain.InspectAsync();

        Assert.True(observation.ConstructorReadRejected);
    }

    [Fact]
    public async Task Attribute_without_a_storage_name_resolves_the_unkeyed_factory()
    {
        var grain = fixture.GrainFactory.GetGrain<IFacetUnkeyedGrain>(Guid.NewGuid());

        Assert.Equal("facet-default", await grain.GetValueAsync());
    }

    [Fact]
    public async Task Raw_persistent_state_facet_is_unaffected()
    {
        var grain = fixture.GrainFactory.GetGrain<IFacetRawGrain>(Guid.NewGuid());

        Assert.Equal("facet-default", await grain.GetValueAsync());
    }

    [Fact]
    public async Task State_type_supplies_its_own_default_from_the_grain_context()
    {
        var key = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IFacetContractGrain>(key);

        Assert.Equal(key, await grain.GetIdAsync());
    }

    [Fact]
    public async Task State_type_configures_the_instance_adopted_at_activation()
    {
        var grain = fixture.GrainFactory.GetGrain<IFacetContractGrain>(Guid.NewGuid());

        Assert.Equal(fixture.TimeProvider.GetUtcNow(), await grain.GetConfiguredNowAsync());
    }

    [Fact]
    public async Task State_type_configures_the_instance_adopted_by_a_read()
    {
        var grain = fixture.GrainFactory.GetGrain<IFacetContractGrain>(Guid.NewGuid());

        Assert.Equal(fixture.TimeProvider.GetUtcNow(), await grain.GetConfiguredNowAfterReadAsync());
    }

    [Fact]
    public async Task State_contract_also_applies_to_an_explicitly_registered_manager()
    {
        var key = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IRegisteredContractGrain>(key);

        var observation = await grain.InspectAsync();

        // The state type's own default and wiring apply however the manager was obtained,
        // and the caller's configuration callback layers on top of the state's.
        Assert.Equal(key, observation.Id);
        Assert.Equal(fixture.TimeProvider.GetUtcNow(), observation.ConfiguredNow);
        Assert.Equal("grain-callback", observation.Label);
    }
}

[GenerateSerializer]
public sealed record FacetState
{
    [Id(0)] public string Value { get; init; } = "facet-default";
}

[GenerateSerializer]
public sealed record ContractState : IConfigurableState, IStateDefault<ContractState>
{
    // Transient wiring restored by Configure on every adopted instance; never persisted.
    [NonSerialized] private TimeProvider? clock;
    [NonSerialized] private string label = "not-labelled";

    [Id(0)] public Guid Id { get; init; }

    public DateTimeOffset? ConfiguredNow => clock?.GetUtcNow();

    public string Label => label;

    public void SetLabel(string value) => label = value;

    public static ContractState CreateDefault(IGrainContext context) =>
        new() { Id = context.GrainId.GetGuidKey() };

    public void Configure(IGrainContext context) =>
        clock = context.ActivationServices.GetRequiredService<TimeProvider>();
}

[GenerateSerializer]
public sealed record FacetObservation(
    [property: Id(0)] string Value,
    [property: Id(1)] string ActivationValue,
    [property: Id(2)] bool ConstructorReadRejected);

[GenerateSerializer]
public sealed record ContractObservation(
    [property: Id(0)] Guid Id,
    [property: Id(1)] DateTimeOffset? ConfiguredNow,
    [property: Id(2)] string Label);

public interface IFacetGrain : IGrainWithGuidKey
{
    Task<FacetObservation> InspectAsync();
    Task SaveAndDeactivateAsync(string value);
}

public sealed class FacetGrain : Grain, IFacetGrain
{
    private readonly IStateManager<FacetState> state;
    private readonly bool constructorReadRejected;
    private string activationValue = "not activated";

    public FacetGrain([PersistentState("facet", "Default")] IStateManager<FacetState> state)
    {
        this.state = state;
        try
        {
            _ = state.State;
        }
        catch (InvalidOperationException)
        {
            constructorReadRejected = true;
        }
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        activationValue = state.State.Value;
        return Task.CompletedTask;
    }

    public Task<FacetObservation> InspectAsync() =>
        Task.FromResult(new FacetObservation(state.State.Value, activationValue, constructorReadRejected));

    public async Task SaveAndDeactivateAsync(string value)
    {
        await state.WriteAsync(state.State with { Value = value });
        DeactivateOnIdle();
    }
}

public interface IFacetUnkeyedGrain : IGrainWithGuidKey
{
    Task<string> GetValueAsync();
}

public sealed class FacetUnkeyedGrain(
    [PersistentState("unkeyed")] IStateManager<FacetState> state) : Grain, IFacetUnkeyedGrain
{
    public Task<string> GetValueAsync() => Task.FromResult(state.State.Value);
}

public interface IFacetRawGrain : IGrainWithGuidKey
{
    Task<string> GetValueAsync();
}

public sealed class FacetRawGrain(
    [PersistentState("raw", "Default")] IPersistentState<FacetState> storage) : Grain, IFacetRawGrain
{
    public Task<string> GetValueAsync() => Task.FromResult(storage.State.Value);
}

public interface IFacetContractGrain : IGrainWithGuidKey
{
    Task<Guid> GetIdAsync();
    Task<DateTimeOffset?> GetConfiguredNowAsync();
    Task<DateTimeOffset?> GetConfiguredNowAfterReadAsync();
}

public sealed class FacetContractGrain(
    [PersistentState("contract", "Default")] IStateManager<ContractState> state)
    : Grain, IFacetContractGrain
{
    public Task<Guid> GetIdAsync() => Task.FromResult(state.State.Id);

    public Task<DateTimeOffset?> GetConfiguredNowAsync() => Task.FromResult(state.State.ConfiguredNow);

    public async Task<DateTimeOffset?> GetConfiguredNowAfterReadAsync()
    {
        await state.ReadAsync();
        return state.State.ConfiguredNow;
    }
}

public interface IRegisteredContractGrain : IGrainWithGuidKey
{
    Task<ContractObservation> InspectAsync();
}

public sealed class RegisteredContractGrain : Grain, IRegisteredContractGrain
{
    private readonly IStateManager<ContractState> state;

    public RegisteredContractGrain(
        [PersistentState("registered", "Default")] IPersistentState<ContractState> storage)
    {
        state = this.RegisterStateManager("Default", storage,
            configureState: loaded => loaded.SetLabel("grain-callback"));
    }

    public Task<ContractObservation> InspectAsync() => Task.FromResult(
        new ContractObservation(state.State.Id, state.State.ConfiguredNow, state.State.Label));
}

public sealed class StateManagerFacetFixture : IAsyncLifetime
{
    private InProcessTestCluster? cluster;

    public ManualTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero));

    public IGrainFactory GrainFactory =>
        (cluster ?? throw new InvalidOperationException("Test cluster not initialized.")).Client;

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("Default");
            siloBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(TimeProvider);
                services.AddDefaultStateManager("Default");
                services.AddDefaultStateManager();
            });
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
}
