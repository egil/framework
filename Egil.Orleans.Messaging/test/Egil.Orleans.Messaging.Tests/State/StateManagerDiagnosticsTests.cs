using Orleans.Storage;

namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerDiagnosticsTests
{
    [Theory]
    [InlineData(StateManagerOperation.Read)]
    [InlineData(StateManagerOperation.Write)]
    [InlineData(StateManagerOperation.Clear)]
    public async Task Successful_operation_resets_a_previous_failure(StateManagerOperation operation)
    {
        var (manager, storage) = await AfterConflictAsync();
        storage.MutationFailure = null;
        var token = TestContext.Current.CancellationToken;

        await (operation switch
        {
            StateManagerOperation.Read => manager.ReadAsync(token),
            StateManagerOperation.Write => manager.WriteAsync(new("successful"), token),
            StateManagerOperation.Clear => manager.ClearAsync(token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        });

        Assert.Null(manager.LastFailureKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_mutation_recovered_as_success_resets_its_failure(bool clear)
    {
        var storage = new RecoveryStorage<Snapshot>(new("stored"))
        {
            MutationFailure = new IOException("Response lost after persisting."),
            PersistBeforeFailure = true,
        };
        IStateManager<Snapshot> manager = new DefaultStateManager<Snapshot>(storage, static () => new("default"),
            recoveryPolicy: StateRecoveryPolicy.ReadBack);
        var token = TestContext.Current.CancellationToken;

        await (clear ? manager.ClearAsync(token) : manager.WriteAsync(new("successful"), token));

        Assert.Equal(1, storage.Reads);
        Assert.Equal(clear ? "default" : "successful", manager.State.Value);
        Assert.Null(manager.LastFailureKind);
    }

    [Fact]
    public async Task A_successful_save_resets_the_failure_after_staging_leaves_it_unchanged()
    {
        var (manager, storage) = await AfterConflictAsync();
        manager.State = new("staged");
        Assert.Equal(StorageFailureKind.Conflict, manager.LastFailureKind);
        storage.MutationFailure = null;

        await manager.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("staged", storage.Persisted!.Value);
        Assert.Null(manager.LastFailureKind);
    }

    [Fact]
    public async Task A_successful_no_op_save_resets_the_failure_without_touching_storage()
    {
        var (manager, storage) = await AfterConflictAsync();
        Assert.False(manager.HasUnsavedChanges);

        await manager.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Null(manager.LastFailureKind);
        Assert.Equal(1, storage.Mutations);
        Assert.Equal(1, storage.Reads);
    }

    [Fact]
    public async Task A_later_failed_mutation_replaces_the_previous_classification()
    {
        var (manager, storage) = await AfterConflictAsync();
        var failure = new IOException("The later write's outcome is unknown.");
        storage.MutationFailure = failure;

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() =>
            manager.WriteAsync(new("later"), TestContext.Current.CancellationToken)));

        Assert.Equal(StorageFailureKind.UnknownOutcome, manager.LastFailureKind);
    }

    [Fact]
    public async Task A_failed_read_does_not_replace_or_reset_the_mutation_classification()
    {
        var (manager, storage) = await AfterConflictAsync();
        var failure = new IOException("Read unavailable.");
        storage.ReadFailure = failure;

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() =>
            manager.ReadAsync(TestContext.Current.CancellationToken)));

        Assert.Equal(StorageFailureKind.Conflict, manager.LastFailureKind);
    }

    [Fact]
    public async Task Configuration_and_rejected_operations_leave_the_failure_unchanged()
    {
        var (manager, storage) = await AfterConflictAsync();
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        manager.ConfigureHooks(_ => { });
        await Assert.ThrowsAsync<OperationCanceledException>(() => manager.WriteAsync(new("canceled"), canceled.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.WriteAsync(null!, TestContext.Current.CancellationToken));

        Assert.Equal(StorageFailureKind.Conflict, manager.LastFailureKind);
        Assert.Equal(1, storage.Mutations);
    }

    [Fact]
    public async Task An_operation_which_throws_from_a_callback_does_not_reset_the_previous_failure()
    {
        var (manager, storage) = await AfterConflictAsync();
        storage.MutationFailure = null;
        var failure = new InvalidOperationException("Application callback failed.");
        manager.ConfigureHooks(hooks => hooks.OnWrite = _ => throw failure);

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.WriteAsync(new("persisted"), TestContext.Current.CancellationToken)));

        Assert.Equal("persisted", storage.Persisted!.Value);
        Assert.Equal(StorageFailureKind.Conflict, manager.LastFailureKind);
    }

    private static async Task<(IStateManager<Snapshot> Manager, RecoveryStorage<Snapshot> Storage)> AfterConflictAsync()
    {
        var storage = new RecoveryStorage<Snapshot>(new("stored"))
        {
            MutationFailure = new InconsistentStateException("Stale ETag."),
        };
        IStateManager<Snapshot> manager = new DefaultStateManager<Snapshot>(storage, static () => new("default"),
            recoveryPolicy: StateRecoveryPolicy.ReadBack);
        await Assert.ThrowsAsync<InconsistentStateException>(() =>
            manager.WriteAsync(new("rejected"), TestContext.Current.CancellationToken));
        Assert.Equal(StorageFailureKind.Conflict, manager.LastFailureKind);
        return (manager, storage);
    }

    private sealed record Snapshot(string Value);
}
