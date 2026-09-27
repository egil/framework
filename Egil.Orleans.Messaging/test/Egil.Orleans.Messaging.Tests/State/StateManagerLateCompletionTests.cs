namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerLateCompletionTests
{
    [Theory]
    [InlineData(StateManagerOperation.Read)]
    [InlineData(StateManagerOperation.Write)]
    [InlineData(StateManagerOperation.Clear)]
    public async Task Storage_completion_after_fencing_rejects_adoption_and_lifecycle_callbacks(StateManagerOperation operation)
    {
        var storage = new DelayedStorage(new("stored"));
        var configurations = 0;
        var notifications = 0;
        var defaults = 0;
        IStateManager<Snapshot> manager = new DefaultStateManager<Snapshot>(storage,
            () => { defaults++; return new("default"); }, _ => configurations++);
        manager.ConfigureHooks(hooks => hooks.OnChange = (_, _, _) => notifications++);
        var token = TestContext.Current.CancellationToken;

        // Overlapping storage calls are unsupported. Exercise only the permanent-fence
        // invariant here: an already pending completion must not reopen caller callbacks.
        var pending = operation switch
        {
            StateManagerOperation.Read => manager.ReadAsync(token),
            StateManagerOperation.Write => manager.WriteAsync(new("pending"), token),
            StateManagerOperation.Clear => manager.ClearAsync(token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        Assert.False(pending.IsCompleted);
        var failure = new IOException("Another storage mutation failed.");
        storage.WriteFailure = failure;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => manager.WriteAsync(new("failed"), token)));
        storage.Complete();

        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => pending);

        Assert.Same(failure, rejected.InnerException);
        Assert.Equal(1, configurations);
        Assert.Equal(0, notifications);
        Assert.Equal(0, defaults);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.State).InnerException);
    }

    private sealed record Snapshot(string Value);

    private sealed class DelayedStorage(Snapshot initial) : IPersistentState<Snapshot>
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Snapshot State { get; set; } = initial;
        public string Etag => "etag";
        public bool RecordExists { get; private set; } = true;
        public Exception? WriteFailure { get; set; }
        public void Complete() => completion.SetResult();

        public Task ReadStateAsync() => ReadStateAsync(CancellationToken.None);
        public Task WriteStateAsync() => WriteStateAsync(CancellationToken.None);
        public Task ClearStateAsync() => ClearStateAsync(CancellationToken.None);

        public async Task ReadStateAsync(CancellationToken cancellationToken)
        {
            await completion.Task.WaitAsync(cancellationToken);
            State = new("loaded");
        }

        public Task WriteStateAsync(CancellationToken cancellationToken)
            => WriteFailure is { } failure ? Task.FromException(failure) : completion.Task.WaitAsync(cancellationToken);

        public async Task ClearStateAsync(CancellationToken cancellationToken)
        {
            await completion.Task.WaitAsync(cancellationToken);
            State = null!;
            RecordExists = false;
        }
    }
}
