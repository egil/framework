namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerHookActivationTests(MessagingTestClusterFixture fixture)
    : IClassFixture<MessagingTestClusterFixture>
{
    [Theory]
    [InlineData(Registration.Injected)]
    [InlineData(Registration.Constructor)]
    [InlineData(Registration.AsyncRegistration)]
    public async Task Initial_handlers_finish_before_activation_for_absent_and_persisted_records(Registration registration)
    {
        var grain = GetGrain(registration, Guid.NewGuid().ToString());

        Assert.Equal("common:default:False,read:default,activated", await grain.ObserveAsync());
        await grain.SaveAndDeactivateAsync();

        Assert.Equal("common:saved:True,read:saved,activated", await grain.ObserveAsync());
    }

    [Theory]
    [InlineData(Registration.Injected)]
    [InlineData(Registration.Constructor)]
    [InlineData(Registration.AsyncRegistration)]
    public async Task Initial_handler_failure_fails_activation(Registration registration)
    {
        var grain = GetGrain(registration, "fail-" + Guid.NewGuid());

        var error = await Assert.ThrowsAnyAsync<Exception>(() => grain.ObserveAsync());

        Assert.Contains("initial hook failed", error.ToString(), StringComparison.Ordinal);
    }

    public enum Registration { Injected, Constructor, AsyncRegistration }

    private IHookActivationGrain GetGrain(Registration registration, string key) => registration switch
    {
        Registration.Injected => fixture.GrainFactory.GetGrain<IInjectedHookGrain>(key),
        Registration.Constructor => fixture.GrainFactory.GetGrain<IConstructorHookGrain>(key),
        Registration.AsyncRegistration => fixture.GrainFactory.GetGrain<IAsyncRegisteredHookGrain>(key),
        _ => throw new ArgumentOutOfRangeException(nameof(registration))
    };
}

public interface IHookActivationGrain : IGrainWithStringKey
{
    Task<string> ObserveAsync();
    Task SaveAndDeactivateAsync();
}

public interface IInjectedHookGrain : IHookActivationGrain;
public interface IConstructorHookGrain : IHookActivationGrain;
public interface IAsyncRegisteredHookGrain : IHookActivationGrain;

[GenerateSerializer]
public sealed record HookActivationState
{
    [Id(0)] public string Value { get; init; } = "default";
}

public abstract class HookActivationGrain : Grain, IHookActivationGrain
{
    protected IStateManager<HookActivationState> Manager { get; set; } = null!;
    private readonly List<string> events = [];
    private StateManagerHooks<HookActivationState>? retainedConfiguration;
    private int configurationCalls;

    protected void ConfigureInitialHooks(StateManagerHooks<HookActivationState> hooks)
    {
        retainedConfiguration = hooks;
        configurationCalls++;
        hooks.OnChangeAsync = async (state, operation, exists, _) =>
        {
            await Task.Yield();
            Assert.Equal(StateManagerOperation.Read, operation);
            events.Add($"common:{state.Value}:{exists}");
        };
        hooks.OnReadAsync = async (state, _) =>
        {
            await Task.Yield();
            if (this.GetPrimaryKeyString().StartsWith("fail-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("initial hook failed");
            }
            events.Add($"read:{state.Value}");
        };
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        events.Add("activated");
        return Task.CompletedTask;
    }

    public Task<string> ObserveAsync()
    {
        Assert.Equal(1, configurationCalls);
        return Task.FromResult(string.Join(',', events));
    }

    protected void ChangeRetainedConfiguration()
    {
        Assert.NotNull(retainedConfiguration);
        retainedConfiguration.OnReadAsync = (_, _) => throw new InvalidOperationException("Retained configuration escaped into activation");
    }

    public async Task SaveAndDeactivateAsync()
    {
        Manager.ConfigureHooks(_ => { });
        await Manager.WriteAsync(new() { Value = "saved" });
        DeactivateOnIdle();
    }
}

public sealed class InjectedHookGrain : HookActivationGrain, IInjectedHookGrain
{
    public InjectedHookGrain([PersistentState("state", "Default")] IStateManager<HookActivationState> manager)
    {
        Manager = manager;
        manager.ConfigureHooks(ConfigureInitialHooks);
        ChangeRetainedConfiguration();
    }
}

public sealed class ConstructorHookGrain : HookActivationGrain, IConstructorHookGrain
{
    public ConstructorHookGrain([PersistentState("state", "Default")] IPersistentState<HookActivationState> storage)
    {
        Manager = this.RegisterStateManager("Default", storage);
        Manager.ConfigureHooks(ConfigureInitialHooks);
        ChangeRetainedConfiguration();
    }
}

public sealed class AsyncRegisteredHookGrain(
    [PersistentState("state", "Default")] IPersistentState<HookActivationState> storage)
    : HookActivationGrain, IAsyncRegisteredHookGrain
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Manager = await this.RegisterStateManagerAsync("Default", storage, ConfigureInitialHooks, cancellationToken: cancellationToken);
        await base.OnActivateAsync(cancellationToken);
    }
}
