namespace Egil.Orleans.Messaging.Tests;

public sealed class StateManagerExtensionsTests
{
    [Fact]
    public void AsStateManager_wraps_persistent_state()
    {
        var storage = new FakePersistentState(new TestState("initial"));

        var manager = storage.AsStateManager();

        Assert.NotNull(manager);
        Assert.Equal(new TestState("initial"), manager.State);
    }

    [Fact]
    public void AsStateManager_throws_for_null_storage()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => StateManagerExtensions.AsStateManager<TestState>(null!));

        Assert.Equal("storage", ex.ParamName);
    }

    private sealed record TestState(string Value);

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
}
