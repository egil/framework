using Microsoft.Extensions.DependencyInjection;

namespace Egil.Orleans.Messaging.Tests;

public sealed class StateManagerExtensionsTests
{
    [Fact]
    public void InitializeStateManagerCore_resolves_keyed_factory_and_wraps_persistent_state()
    {
        var storageName = "state";
        var services = new ServiceCollection();
        services.AddDefaultStateManager(storageName);
        var provider = services.BuildServiceProvider();
        var storage = new FakePersistentState(new TestState("initial"));

        var manager = StateManagerExtensions.InitializeStateManagerCore(
            provider,
            storageName,
            storage,
            typeof(StateManagerExtensionsTests));

        Assert.NotNull(manager);
        Assert.Equal(new TestState("initial"), manager.State);
    }

    [Fact]
    public void InitializeStateManagerCore_throws_for_missing_keyed_registration()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var storage = new FakePersistentState(new TestState("initial"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StateManagerExtensions.InitializeStateManagerCore(
                provider,
                "missing",
                storage,
                typeof(StateManagerExtensionsTests)));

        Assert.Contains("missing", ex.Message);
        Assert.Contains(typeof(TestState).FullName!, ex.Message);
        Assert.Contains(typeof(StateManagerExtensionsTests).FullName!, ex.Message);
    }

    [Fact]
    public void InitializeStateManager_throws_for_null_grain()
    {
        var storage = new FakePersistentState(new TestState("initial"));

        var ex = Assert.Throws<ArgumentNullException>(() =>
            StateManagerExtensions.InitializeStateManager<TestGrainBase, TestState>(null!, "state", storage));

        Assert.Equal("grain", ex.ParamName);
    }

    [Fact]
    public void InitializeStateManager_throws_for_empty_storage_name()
    {
        var grain = new FakeGrainBase();
        var storage = new FakePersistentState(new TestState("initial"));

        var ex = Assert.Throws<ArgumentException>(() =>
            grain.InitializeStateManager(" ", storage));

        Assert.Equal("storageName", ex.ParamName);
    }

    [Fact]
    public void InitializeStateManager_throws_for_null_storage()
    {
        var grain = new FakeGrainBase();

        var ex = Assert.Throws<ArgumentNullException>(() =>
            grain.InitializeStateManager<TestGrainBase, TestState>("state", null!));

        Assert.Equal("storage", ex.ParamName);
    }

    private sealed record TestState(string Value) : IEquatable<TestState>;

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
