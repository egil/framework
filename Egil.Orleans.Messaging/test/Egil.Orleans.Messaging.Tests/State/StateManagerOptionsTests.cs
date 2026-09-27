using Microsoft.Extensions.Options;

namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerOptionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Layers_override_in_order_regardless_of_helper_registration_order(bool globalFirst)
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        void Global(StateManagerOptions options)
        {
            calls.Add("global");
            options.RecoveryPolicy = StateRecoveryPolicy.ReadBack;
        }
        void Factory(StateManagerOptions options)
        {
            Assert.Equal(StateRecoveryPolicy.ReadBack, options.RecoveryPolicy);
            calls.Add("factory");
            options.RecoveryPolicy = StateRecoveryPolicy.FenceAndDeactivate;
        }
        if (globalFirst) services.ConfigureStateManager(Global);
        services.AddStateManagerFactory<RecordingFactory>("Orders", Factory);
        if (!globalFirst) services.ConfigureStateManager(Global);
        using var provider = services.BuildServiceProvider();
        var grain = new RegistrationGrain(new RecoveryGrainContext(provider));

        grain.RegisterStateManager("Orders", new RecoveryStorage<Snapshot>(new()), configure: options =>
        {
            Assert.Equal(StateRecoveryPolicy.FenceAndDeactivate, options.RecoveryPolicy);
            calls.Add("grain");
            options.RecoveryPolicy = StateRecoveryPolicy.ReadBack;
        });

        var received = provider.GetRequiredKeyedService<IStateManagerFactory>("Orders");
        var factory = Assert.IsType<RecordingFactory>(received);
        Assert.Equal(StateRecoveryPolicy.ReadBack, Assert.Single(factory.Options).RecoveryPolicy);
        Assert.Same(grain.GrainContext, factory.Context);
        Assert.Equal(["global", "factory", "grain"], calls);
    }

    [Fact]
    public void Repeated_callbacks_run_in_order_and_names_and_managers_are_isolated()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.ConfigureStateManager(_ => calls.Add("global-1"));
        services.ConfigureStateManager((options, _) => { calls.Add("global-2"); options.RecoveryPolicy = StateRecoveryPolicy.ReadBack; });
        services.AddStateManagerFactory<RecordingFactory>("Orders", _ => calls.Add("orders-1"));
        services.AddStateManagerFactory<RecordingFactory>("Orders", options => { calls.Add("orders-2"); options.RecoveryPolicy = StateRecoveryPolicy.FenceAndDeactivate; });
        services.AddStateManagerFactory<RecordingFactory>();
        services.AddStateManagerFactory<RecordingFactory>("Other");
        using var provider = services.BuildServiceProvider();
        var grain = new RegistrationGrain(new RecoveryGrainContext(provider));
        StateManagerOptions? retained = null;

        grain.RegisterStateManager("Orders", new RecoveryStorage<Snapshot>(new()), configure: options => retained = options);
        Assert.Equal(["global-1", "global-2", "orders-1", "orders-2"], calls);
        retained!.RecoveryPolicy = StateRecoveryPolicy.ReadBack;
        grain.RegisterStateManager("Orders", new RecoveryStorage<Snapshot>(new()));
        grain.RegisterStateManager(new RecoveryStorage<Snapshot>(new()));
        grain.RegisterStateManager("Other", new RecoveryStorage<Snapshot>(new()));

        var orders = Assert.IsType<RecordingFactory>(provider.GetRequiredKeyedService<IStateManagerFactory>("Orders"));
        Assert.Equal(2, orders.Options.Count);
        Assert.NotSame(orders.Options[0], orders.Options[1]);
        Assert.All(orders.Options, options => Assert.Equal(StateRecoveryPolicy.FenceAndDeactivate, options.RecoveryPolicy));
        Assert.Equal(StateRecoveryPolicy.ReadBack, Assert.Single(Assert.IsType<RecordingFactory>(provider.GetRequiredService<IStateManagerFactory>()).Options).RecoveryPolicy);
        Assert.Equal(StateRecoveryPolicy.ReadBack, Assert.Single(Assert.IsType<RecordingFactory>(provider.GetRequiredKeyedService<IStateManagerFactory>("Other")).Options).RecoveryPolicy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_factory_receives_grain_override_through_sync_and_async_registration(bool asynchronous)
    {
        var services = new ServiceCollection().AddDefaultStateManager("Orders");
        using var provider = services.BuildServiceProvider();
        var grain = new RegistrationGrain(new RecoveryGrainContext(provider));
        var storage = new RecoveryStorage<Snapshot>(new()) { MutationFailure = new IOException(), PersistBeforeFailure = true };
        var manager = asynchronous
            ? await grain.RegisterStateManagerAsync("Orders", storage, _ => { }, configure: options => options.RecoveryPolicy = StateRecoveryPolicy.ReadBack, cancellationToken: TestContext.Current.CancellationToken)
            : grain.RegisterStateManager("Orders", storage, configure: options => options.RecoveryPolicy = StateRecoveryPolicy.ReadBack);

        await manager.WriteAsync(new("committed"), TestContext.Current.CancellationToken);

        Assert.Equal("committed", manager.State.Value);
        Assert.Equal(1, storage.Reads);
    }

    [Fact]
    public void Provider_callbacks_resolve_from_their_own_silo_and_default_is_fencing()
    {
        using var readBack = BuildProvider(StateRecoveryPolicy.ReadBack);
        using var fence = BuildProvider(StateRecoveryPolicy.FenceAndDeactivate);
        var first = new RegistrationGrain(new RecoveryGrainContext(readBack));
        var second = new RegistrationGrain(new RecoveryGrainContext(fence));

        first.RegisterStateManager(new RecoveryStorage<Snapshot>(new()));
        second.RegisterStateManager(new RecoveryStorage<Snapshot>(new()));

        Assert.Equal(StateRecoveryPolicy.ReadBack, Assert.Single(Assert.IsType<RecordingFactory>(readBack.GetRequiredService<IStateManagerFactory>()).Options).RecoveryPolicy);
        Assert.Equal(StateRecoveryPolicy.FenceAndDeactivate, Assert.Single(Assert.IsType<RecordingFactory>(fence.GetRequiredService<IStateManagerFactory>()).Options).RecoveryPolicy);
        Assert.Equal(StateRecoveryPolicy.FenceAndDeactivate, new StateManagerOptions().RecoveryPolicy);
    }

    [Fact]
    public void Invalid_final_configuration_is_rejected_before_factory_or_state_callbacks()
    {
        var services = new ServiceCollection().AddStateManagerFactory<RecordingFactory>();
        using var provider = services.BuildServiceProvider();
        var grain = new RegistrationGrain(new RecoveryGrainContext(provider));
        var configured = false;

        Assert.Throws<OptionsValidationException>(() => grain.RegisterStateManager(new RecoveryStorage<Snapshot>(new()),
            configureState: _ => configured = true, configure: options => options.RecoveryPolicy = (StateRecoveryPolicy)42));

        Assert.False(configured);
        Assert.Empty(Assert.IsType<RecordingFactory>(provider.GetRequiredService<IStateManagerFactory>()).Options);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Every_registration_shape_resolves_local_options_and_preserves_its_lifetime_snapshot(int shape)
    {
        var services = new ServiceCollection().AddDefaultStateManager().AddDefaultStateManager("Orders");
        using var provider = services.BuildServiceProvider();
        var grain = new RegistrationGrain(new RecoveryGrainContext(provider));
        var storage = new RecoveryStorage<Snapshot>(new()) { MutationFailure = new IOException(), PersistBeforeFailure = true };
        StateManagerOptions? retained = null;
        void Configure(StateManagerOptions options)
        {
            options.RecoveryPolicy = StateRecoveryPolicy.ReadBack;
            retained = options;
        }
        var token = TestContext.Current.CancellationToken;
        var manager = shape switch
        {
            0 => grain.RegisterStateManager(storage, configure: Configure),
            1 => grain.RegisterStateManager("Orders", storage, configure: Configure),
            2 => grain.RegisterStateManager(storage, () => new(), configure: Configure),
            3 => grain.RegisterStateManager("Orders", storage, () => new(), configure: Configure),
            4 => await grain.RegisterStateManagerAsync(storage, _ => { }, configure: Configure, cancellationToken: token),
            5 => await grain.RegisterStateManagerAsync("Orders", storage, _ => { }, configure: Configure, cancellationToken: token),
            6 => await grain.RegisterStateManagerAsync(storage, () => new(), _ => { }, configure: Configure, cancellationToken: token),
            7 => await grain.RegisterStateManagerAsync("Orders", storage, () => new(), _ => { }, configure: Configure, cancellationToken: token),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        retained!.RecoveryPolicy = StateRecoveryPolicy.FenceAndDeactivate;

        await manager.WriteAsync(new("committed"), token);

        Assert.Equal("committed", manager.State.Value);
        Assert.Equal(1, storage.Reads);
    }

    private static ServiceProvider BuildProvider(StateRecoveryPolicy policy)
        => new ServiceCollection().AddSingleton(new PolicyChoice(policy))
            .ConfigureStateManager((options, provider) => options.RecoveryPolicy = provider.GetRequiredService<PolicyChoice>().Policy)
            .AddStateManagerFactory<RecordingFactory>().BuildServiceProvider();

    private sealed record PolicyChoice(StateRecoveryPolicy Policy);
    private sealed record Snapshot(string Value)
    {
        public Snapshot() : this("stored") { }
    }
    private sealed class RegistrationGrain(IGrainContext context) : IGrainBase
    {
        public IGrainContext GrainContext => context;
    }

    private sealed class RecordingFactory : IStateManagerFactory
    {
        public List<StateManagerOptions> Options { get; } = [];
        public IGrainContext? Context { get; private set; }
        public IStateManager<T> Create<T>(IPersistentState<T> storage, Func<T> createInitialState, StateManagerOptions options,
            Action<T>? configureState = null, IGrainContext? grainContext = null) where T : class, IEquatable<T>
        {
            Options.Add(options);
            Context = grainContext;
            return new DefaultStateManager<T>(storage, createInitialState, configureState, options.RecoveryPolicy, grainContext);
        }
    }
}
