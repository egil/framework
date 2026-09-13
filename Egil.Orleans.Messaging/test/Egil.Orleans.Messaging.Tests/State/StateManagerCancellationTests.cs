namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerCancellationTests
{
    [Theory]
    [InlineData(StorageOperation.Write)]
    [InlineData(StorageOperation.Clear)]
    public async Task Confirmed_success_is_adopted_when_cancellation_arrives_after_persistence(StorageOperation operation)
    {
        using var cancellation = new CancellationTokenSource();
        using var storage = new CancellableStorage(operation) { CancelAfterPersisting = cancellation };
        storage.Release.TrySetResult();
        IStateManager<Snapshot> manager = new DefaultStateManager<Snapshot>(storage, static () => new("default"));

        await Invoke(manager, operation, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(new Snapshot(operation == StorageOperation.Write ? "next" : "default"), manager.State);
    }

    [Theory]
    [InlineData(StorageOperation.Read)]
    [InlineData(StorageOperation.Write)]
    [InlineData(StorageOperation.Clear)]
    public async Task Cancellation_before_start_leaves_snapshot_and_storage_untouched(StorageOperation operation)
    {
        using var storage = new CancellableStorage(operation);
        IStateManager<Snapshot> manager = new DefaultStateManager<Snapshot>(storage, static () => new("default"));
        var previous = manager.State;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        storage.Release.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Invoke(manager, operation, cancellation.Token));

        Assert.Equal(0, storage.Calls);
        Assert.Same(previous, manager.State);
        Assert.Same(previous, storage.State);
    }

    [Theory]
    [InlineData(StorageOperation.Read)]
    [InlineData(StorageOperation.Write)]
    [InlineData(StorageOperation.Clear)]
    public async Task Cancellation_interrupts_storage_without_publishing_uncommitted_state(StorageOperation operation)
    {
        using var storage = new CancellableStorage(operation);
        IStateManager<Snapshot> manager = new DefaultStateManager<Snapshot>(storage, static () => new("default"));
        var previous = manager.State;
        using var cancellation = new CancellationTokenSource();
        var pending = Invoke(manager, operation, cancellation.Token);
        await storage.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Same(previous, manager.State);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Same(previous, manager.State);
        Assert.Same(previous, storage.State);
        Assert.Equal(1, storage.Calls);
    }

    [Theory]
    [InlineData(StorageOperation.Write)]
    [InlineData(StorageOperation.Clear)]
    public async Task Cancellation_interrupts_recovery_and_a_fresh_read_restores_durable_state(StorageOperation operation)
    {
        using var storage = new CancellableStorage(StorageOperation.Read) { LoseResponse = true };
        IStateManager<Snapshot> manager = new DefaultStateManager<Snapshot>(storage, static () => new("default"));
        var previous = manager.State;
        using var cancellation = new CancellationTokenSource();
        var pending = Invoke(manager, operation, cancellation.Token);
        await storage.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        await cancellation.CancelAsync();
        var error = await Assert.ThrowsAsync<IOException>(() =>
            pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Same(storage.LostResponse, error);
        Assert.Same(previous, manager.State);
        storage.Release.TrySetResult();
        await manager.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new Snapshot(operation == StorageOperation.Write ? "next" : "default"), manager.State);
    }

    private static Task Invoke(IStateManager<Snapshot> manager, StorageOperation operation, CancellationToken cancellationToken) => operation switch
    {
        StorageOperation.Read => manager.ReadAsync(cancellationToken),
        StorageOperation.Write => manager.WriteAsync(new Snapshot("next"), cancellationToken),
        StorageOperation.Clear => manager.ClearAsync(cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    public enum StorageOperation
    {
        Read,
        Write,
        Clear
    }

    private sealed record Snapshot(string Value);

    private sealed class CancellableStorage(StorageOperation blockedOperation) : IPersistentState<Snapshot>, IDisposable
    {
        private Snapshot? persisted = new("previous");
        public Snapshot State { get; set; } = new("previous");
        public string Etag => "etag";
        public bool RecordExists => persisted is not null;
        public int Calls { get; private set; }
        public bool LoseResponse { get; init; }
        public CancellationTokenSource? CancelAfterPersisting { get; init; }
        public IOException LostResponse { get; } = new("Storage response was lost.");
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStateAsync() => ReadStateAsync(CancellationToken.None);
        public Task WriteStateAsync() => WriteStateAsync(CancellationToken.None);
        public Task ClearStateAsync() => ClearStateAsync(CancellationToken.None);

        public async Task ReadStateAsync(CancellationToken cancellationToken)
        {
            await BeforeOperationAsync(StorageOperation.Read, cancellationToken);
            State = persisted!;
        }

        public async Task WriteStateAsync(CancellationToken cancellationToken)
        {
            await BeforeOperationAsync(StorageOperation.Write, cancellationToken);
            persisted = State;
            if (CancelAfterPersisting is not null)
            {
                await CancelAfterPersisting.CancelAsync();
            }
            if (LoseResponse)
            {
                throw LostResponse;
            }
        }

        public async Task ClearStateAsync(CancellationToken cancellationToken)
        {
            await BeforeOperationAsync(StorageOperation.Clear, cancellationToken);
            persisted = null;
            if (CancelAfterPersisting is not null)
            {
                await CancelAfterPersisting.CancelAsync();
            }
            if (LoseResponse)
            {
                throw LostResponse;
            }
        }

        private async Task BeforeOperationAsync(StorageOperation operation, CancellationToken cancellationToken)
        {
            Calls++;
            if (operation == blockedOperation)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
        }

        public void Dispose() => Release.TrySetResult();
    }
}
