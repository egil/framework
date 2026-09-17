namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerExtensionsTests
{
    [Fact]
    public void RegisterStateManagerCore_resolves_keyed_factory_and_wraps_persistent_state()
    {
        var storageName = "state";
        var services = new ServiceCollection();
        services.AddDefaultStateManager(storageName);
        var provider = services.BuildServiceProvider();
        var storage = new FakePersistentState(new TestState("initial"));

        var manager = StateManagerExtensions.RegisterStateManagerCore(
            new FakeGrainContext(provider),
            storageName,
            storage,
            typeof(StateManagerExtensionsTests), static () => new TestState("default"));

        Assert.NotNull(manager);
        Assert.Equal(new TestState("initial"), manager.State);
    }

    [Fact]
    public void RegisterStateManagerCore_resolves_the_default_factory_when_no_name_is_given()
    {
        var services = new ServiceCollection();
        services.AddDefaultStateManager();
        var provider = services.BuildServiceProvider();
        var storage = new FakePersistentState(new TestState("initial"));

        var manager = StateManagerExtensions.RegisterStateManagerCore(
            new FakeGrainContext(provider),
            storageName: null,
            storage,
            typeof(StateManagerExtensionsTests), static () => new TestState("default"));

        Assert.NotNull(manager);
        Assert.Equal(new TestState("initial"), manager.State);
    }

    [Fact]
    public void RegisterStateManagerCore_throws_for_a_missing_default_registration()
    {
        // A keyed registration does not satisfy the nameless path: the whole point of
        // naming is to pick one of several, and guessing which would be worse than saying so.
        var services = new ServiceCollection();
        services.AddDefaultStateManager("Default");
        var provider = services.BuildServiceProvider();
        var storage = new FakePersistentState(new TestState("initial"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StateManagerExtensions.RegisterStateManagerCore(
                new FakeGrainContext(provider),
                storageName: null,
                storage,
                typeof(StateManagerExtensionsTests), static () => new TestState("default")));

        Assert.Contains("No default IStateManagerFactory", ex.Message, StringComparison.Ordinal);
        Assert.Contains("AddDefaultStateManager()", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterStateManagerCore_throws_for_missing_keyed_registration()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var storage = new FakePersistentState(new TestState("initial"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StateManagerExtensions.RegisterStateManagerCore(
                new FakeGrainContext(provider),
                "missing",
                storage,
                typeof(StateManagerExtensionsTests), static () => new TestState("default")));

        Assert.Contains("missing", ex.Message);
        Assert.Contains(typeof(TestState).FullName!, ex.Message);
        Assert.Contains(typeof(StateManagerExtensionsTests).FullName!, ex.Message);
    }

    [Fact]
    public void RegisterStateManager_throws_for_null_grain()
    {
        var storage = new FakePersistentState(new TestState("initial"));

        var ex = Assert.Throws<ArgumentNullException>(() =>
            StateManagerExtensions.RegisterStateManager<TestGrainBase, TestState>(null!, "state", storage, static () => new TestState("default")));

        Assert.Equal("grain", ex.ParamName);
    }

    [Fact]
    public void RegisterStateManagerCore_describes_why_an_abstract_state_type_has_no_default()
    {
        // Same hazard as the injected path: an abstract type can declare a public
        // parameterless constructor and still be unusable, so the message has to name the
        // requirement rather than report a missing constructor.
        var services = new ServiceCollection();
        services.AddDefaultStateManager();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StateManagerExtensions.RegisterStateManagerCore(
                new FakeGrainContext(provider),
                storageName: null,
                new FakeAbstractPersistentState(),
                typeof(StateManagerExtensionsTests),
                createInitialState: null));

        Assert.Contains(typeof(AbstractTestState).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains("non-abstract type with a public parameterless constructor", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_second_argument_still_selects_the_state_factory_overload()
    {
        // The overloads that take no state factory gained an optional Action<TState>, which
        // makes them applicable to calls that previously only matched the Func<TState>
        // overload. Overload resolution still prefers the factory overload, so an existing
        // RegisterStateManager(storage, null) keeps meaning "null state factory" and keeps
        // failing the same way, rather than silently becoming "null configuration".
        var grain = new FakeGrainBase();
        var storage = new FakePersistentState(new TestState("initial"));

        var ex = Assert.Throws<ArgumentNullException>(() =>
            grain.RegisterStateManager<TestGrainBase, TestState>(storage, null!));

        Assert.Equal("createInitialState", ex.ParamName);
    }

    [Fact]
    public void A_null_third_argument_still_selects_the_state_factory_overload()
    {
        var grain = new FakeGrainBase();
        var storage = new FakePersistentState(new TestState("initial"));

        var ex = Assert.Throws<ArgumentNullException>(() =>
            grain.RegisterStateManager<TestGrainBase, TestState>("state", storage, null!));

        Assert.Equal("createInitialState", ex.ParamName);
    }

    [Fact]
    public void RegisterStateManager_throws_for_empty_storage_name()
    {
        var grain = new FakeGrainBase();
        var storage = new FakePersistentState(new TestState("initial"));

        var ex = Assert.Throws<ArgumentException>(() =>
            grain.RegisterStateManager(" ", storage, static () => new TestState("default")));

        Assert.Equal("storageName", ex.ParamName);
    }

    [Fact]
    public void RegisterStateManager_throws_for_null_storage()
    {
        var grain = new FakeGrainBase();

        var ex = Assert.Throws<ArgumentNullException>(() =>
            grain.RegisterStateManager<TestGrainBase, TestState>("state", null!, static () => new TestState("default")));

        Assert.Equal("storage", ex.ParamName);
    }

    // RegisterStateManagerCore reads only ActivationServices off the context; the rest of
    // IGrainContext is runtime surface these registration tests never reach.
    private sealed class FakeGrainContext(IServiceProvider services) : IGrainContext
    {
        public IServiceProvider ActivationServices { get; } = services;

        public GrainReference GrainReference => throw new NotSupportedException();
        public GrainId GrainId => throw new NotSupportedException();
        public object? GrainInstance => null;
        public ActivationId ActivationId => throw new NotSupportedException();
        public GrainAddress Address => throw new NotSupportedException();
        public IGrainLifecycle ObservableLifecycle => throw new NotSupportedException();
        public IWorkItemScheduler Scheduler => throw new NotSupportedException();
        public Task Deactivated => throw new NotSupportedException();

        public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void ReceiveMessage(object message) => throw new NotSupportedException();

        public void Rehydrate(IRehydrationContext context) => throw new NotSupportedException();

        public void SetComponent<TComponent>(TComponent? value) where TComponent : class
            => throw new NotSupportedException();

        public TComponent? GetComponent<TComponent>() where TComponent : class
            => throw new NotSupportedException();

        public TTarget GetTarget<TTarget>() where TTarget : class => throw new NotSupportedException();

        public object GetComponent(Type componentType) => throw new NotSupportedException();

        public object GetTarget() => throw new NotSupportedException();

        public bool Equals(IGrainContext? other) => ReferenceEquals(this, other);
    }

    private sealed record TestState(string Value) : IEquatable<TestState>;

    public abstract class AbstractTestState : IEquatable<AbstractTestState>
    {
        public AbstractTestState()
        {
        }

        public bool Equals(AbstractTestState? other) => ReferenceEquals(this, other);

        public override bool Equals(object? obj) => Equals(obj as AbstractTestState);

        public override int GetHashCode() => 0;
    }

    private sealed class FakeAbstractPersistentState : IPersistentState<AbstractTestState>
    {
        public string Etag { get; set; } = "etag-1";
        public bool RecordExists { get; set; }
        public AbstractTestState State { get; set; } = null!;

        public Task ReadStateAsync() => Task.CompletedTask;
        public Task WriteStateAsync() => Task.CompletedTask;
        public Task ClearStateAsync() => Task.CompletedTask;
        public Task ReadStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePersistentState(TestState state) : IPersistentState<TestState>
    {
        public string Etag { get; set; } = "etag-1";
        public bool RecordExists { get; set; } = true;
        public TestState State { get; set; } = state;

        public Task ReadStateAsync() => Task.CompletedTask;
        public Task WriteStateAsync() => Task.CompletedTask;

        public Task ClearStateAsync()
        {
            State = null!;
            RecordExists = false;
            return Task.CompletedTask;
        }

        public Task ReadStateAsync(CancellationToken cancellationToken) => ReadStateAsync();
        public Task WriteStateAsync(CancellationToken cancellationToken) => WriteStateAsync();
        public Task ClearStateAsync(CancellationToken cancellationToken) => ClearStateAsync();
    }

    private sealed class FakeGrainBase : TestGrainBase
    {
        public override IGrainContext GrainContext =>
            throw new InvalidOperationException("GrainContext should not be used for guard tests.");
    }

    private abstract class TestGrainBase : IGrainBase
    {
        public abstract IGrainContext GrainContext { get; }

        public virtual Task OnActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public virtual Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}