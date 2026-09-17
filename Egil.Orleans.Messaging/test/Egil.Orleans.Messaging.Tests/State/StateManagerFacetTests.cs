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
    public async Task Attribute_with_a_blank_storage_name_resolves_the_unkeyed_factory()
    {
        // Orleans reads a blank storage name as "the default provider" when it builds the
        // facet, so the manager lookup has to agree rather than hunt for a keyed factory
        // registered under an empty string.
        var grain = fixture.GrainFactory.GetGrain<IFacetBlankStorageGrain>(Guid.NewGuid());

        Assert.Equal("facet-default", await grain.GetValueAsync());
    }

    [Fact]
    public async Task Attribute_with_a_whitespace_storage_name_resolves_the_unkeyed_factory()
    {
        // Orleans' PersistentStateFactory selects the default IGrainStorage on
        // IsNullOrWhiteSpace, so whitespace has to reach the unkeyed factory too. Matching
        // IsNullOrEmpty instead would send the facet to default storage and then hunt for a
        // keyed factory registered under "  ".
        var grain = fixture.GrainFactory.GetGrain<IFacetWhitespaceStorageGrain>(Guid.NewGuid());

        Assert.Equal("facet-default", await grain.GetValueAsync());
    }

    [Fact]
    public async Task A_failed_configuration_leaves_the_newly_adopted_snapshot_visible()
    {
        var grain = fixture.GrainFactory.GetGrain<IConfigureFailureGrain>(Guid.NewGuid());
        await grain.SaveAsync("stored");

        // Configuration runs after the snapshot is published, so a throwing Configure
        // reports the failure without reverting to the last stored value.
        Assert.Equal("configure-default", await grain.ValueAfterFailedConfigureOnClearAsync());
    }

    [Fact]
    public async Task A_missing_factory_registration_reports_its_own_diagnostic()
    {
        var grain = fixture.GrainFactory.GetGrain<IFacetUnmanagedStorageGrain>(Guid.NewGuid());

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => grain.GetValueAsync());

        // The mapper builds the manager through a generic method. Reaching it with
        // MethodInfo.Invoke would wrap this diagnostic in a TargetInvocationException and
        // bury the one sentence that says what to register.
        Assert.Contains("No keyed IStateManagerFactory", ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(System.Reflection.TargetInvocationException), ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_state_type_that_cannot_represent_an_absent_record_fails_on_first_activation()
    {
        // The fixture's cluster deployed with this grain type present, which is itself the
        // assertion about timing: Orleans builds a grain type's constructor argument
        // factory on first activation, not at silo startup, so the guard runs there.
        var grain = fixture.GrainFactory.GetGrain<IFacetUndefaultableGrain>(Guid.NewGuid());

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => grain.GetValueAsync());

        Assert.Contains(typeof(UndefaultableFacetState).FullName!, ex.ToString(), StringComparison.Ordinal);
        Assert.Contains("cannot represent an absent storage record", ex.ToString(), StringComparison.Ordinal);
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
    public async Task State_type_standing_in_for_a_parameterless_constructor_needs_no_state_factory()
    {
        // IStateDefault<TSelf> exists to represent an absent record in place of a public
        // parameterless constructor, so requiring one as well would defeat it.
        var key = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IRegisteredKeyedOnlyGrain>(key);

        Assert.Equal(key, await grain.GetIdAsync());
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

// Has no public parameterless constructor; IStateDefault is the only way it can
// represent an absent record.
[GenerateSerializer]
public sealed record KeyedOnlyState([property: Id(0)] Guid Id) : IStateDefault<KeyedOnlyState>
{
    public static KeyedOnlyState CreateDefault(IGrainContext context) =>
        new(context.GrainId.GetGuidKey());
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

public interface IFacetBlankStorageGrain : IGrainWithGuidKey
{
    Task<string> GetValueAsync();
}

public sealed class FacetBlankStorageGrain(
    [PersistentState("blank", "")] IStateManager<FacetState> state) : Grain, IFacetBlankStorageGrain
{
    public Task<string> GetValueAsync() => Task.FromResult(state.State.Value);
}

public sealed class ConfigureFailureSwitch
{
    public bool ShouldThrow { get; set; }
}

[GenerateSerializer]
public sealed record ConfigureFailureState : IConfigurableState
{
    [Id(0)] public string Value { get; init; } = "configure-default";

    public void Configure(IGrainContext context)
    {
        if (context.ActivationServices.GetRequiredService<ConfigureFailureSwitch>().ShouldThrow)
        {
            throw new InvalidOperationException("Configuration failed.");
        }
    }
}

public interface IConfigureFailureGrain : IGrainWithGuidKey
{
    Task SaveAsync(string value);
    Task<string> ValueAfterFailedConfigureOnClearAsync();
}

public sealed class ConfigureFailureGrain(
    [PersistentState("configure-failure", "Default")] IStateManager<ConfigureFailureState> state,
    ConfigureFailureSwitch failureSwitch) : Grain, IConfigureFailureGrain
{
    public Task SaveAsync(string value) => state.WriteAsync(state.State with { Value = value });

    public async Task<string> ValueAfterFailedConfigureOnClearAsync()
    {
        // A clear adopts a fresh default, so the value that ends up visible distinguishes
        // "keep the published snapshot" from "revert to the last stored one".
        failureSwitch.ShouldThrow = true;
        try
        {
            await state.ClearAsync();
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            failureSwitch.ShouldThrow = false;
        }

        return state.State.Value;
    }
}

// Positional record: no public parameterless constructor, and no IStateDefault.
[GenerateSerializer]
public sealed record UndefaultableFacetState([property: Id(0)] string Value);

public interface IFacetUndefaultableGrain : IGrainWithGuidKey
{
    Task<string> GetValueAsync();
}

public sealed class FacetUndefaultableGrain(
    [PersistentState("undefaultable", "Default")] IStateManager<UndefaultableFacetState> state)
    : Grain, IFacetUndefaultableGrain
{
    public Task<string> GetValueAsync() => Task.FromResult(state.State.Value);
}

// Storage provider exists, but no IStateManagerFactory is keyed to it.
public interface IFacetUnmanagedStorageGrain : IGrainWithGuidKey
{
    Task<string> GetValueAsync();
}

public sealed class FacetUnmanagedStorageGrain(
    [PersistentState("unmanaged", "Unmanaged")] IStateManager<FacetState> state)
    : Grain, IFacetUnmanagedStorageGrain
{
    public Task<string> GetValueAsync() => Task.FromResult(state.State.Value);
}

public interface IFacetWhitespaceStorageGrain : IGrainWithGuidKey
{
    Task<string> GetValueAsync();
}

public sealed class FacetWhitespaceStorageGrain(
    [PersistentState("whitespace", "  ")] IStateManager<FacetState> state)
    : Grain, IFacetWhitespaceStorageGrain
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

public interface IRegisteredKeyedOnlyGrain : IGrainWithGuidKey
{
    Task<Guid> GetIdAsync();
}

public sealed class RegisteredKeyedOnlyGrain : Grain, IRegisteredKeyedOnlyGrain
{
    private readonly IStateManager<KeyedOnlyState> state;

    public RegisteredKeyedOnlyGrain(
        [PersistentState("keyed-only", "Default")] IPersistentState<KeyedOnlyState> storage)
        => state = this.RegisterStateManager("Default", storage);

    public Task<Guid> GetIdAsync() => Task.FromResult(state.State.Id);
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
            siloBuilder.AddMemoryGrainStorage("Unmanaged");
            siloBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(TimeProvider);
                services.AddSingleton<ConfigureFailureSwitch>();
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
