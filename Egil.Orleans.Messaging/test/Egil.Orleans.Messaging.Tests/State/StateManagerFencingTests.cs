namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerFencingTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Failed_mutation_preserves_local_inspection_and_fences_further_changes(bool clear, bool persisted, bool staged)
    {
        var failure = new IOException("The storage response was lost.");
        var storage = new RecoveryStorage<Snapshot>(new("stored"))
        {
            MutationFailure = failure,
            PersistBeforeFailure = persisted,
        };
        IStateManager<Snapshot> manager = new DefaultStateManager<Snapshot>(storage, static () => new("default"));
        if (staged)
            manager.State = new("staged");
        var visibleSnapshot = manager.State;

        var error = await Assert.ThrowsAsync<IOException>(() => clear
            ? manager.ClearAsync(TestContext.Current.CancellationToken)
            : manager.WriteAsync(new("candidate"), TestContext.Current.CancellationToken));

        Assert.Same(failure, error);
        Assert.Equal(0, storage.Reads);
        Assert.Same(visibleSnapshot, manager.State);
        Assert.Equal(staged, manager.HasUnsavedChanges);
        var fenced = Assert.Throws<StateManagerFencedException>(() => manager.State = new("rejected"));
        Assert.Same(failure, fenced.InnerException);
        Assert.Equal(StorageFailureKind.UnknownOutcome, fenced.FailureKind);
        Assert.Contains("fenced", fenced.Message, StringComparison.Ordinal);
        Assert.Same(failure, Assert.Throws<StateManagerFencedException>(() => manager.State = null!).InnerException);
        Assert.Same(failure, Assert.Throws<StateManagerFencedException>(() => manager.ConfigureHooks(null!)).InnerException);
        var callbackRan = false;
        Assert.Throws<StateManagerFencedException>(() => manager.ConfigureHooks(_ => callbackRan = true));
        Assert.False(callbackRan);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        Assert.Same(failure, (await Assert.ThrowsAsync<StateManagerFencedException>(() => manager.ReadAsync(canceled.Token))).InnerException);
        Assert.Same(failure, (await Assert.ThrowsAsync<StateManagerFencedException>(() => manager.ClearAsync(canceled.Token))).InnerException);
        Assert.Same(failure, (await Assert.ThrowsAsync<StateManagerFencedException>(() => manager.WriteAsync(null!, canceled.Token))).InnerException);
        Assert.Same(failure, (await Assert.ThrowsAsync<StateManagerFencedException>(() => manager.SaveChangesAsync(canceled.Token))).InnerException);
        Assert.Equal(1, storage.Mutations);
        Assert.Equal(0, storage.Reads);
        Assert.Equal(StorageFailureKind.UnknownOutcome, manager.LastFailureKind);
        Assert.Equal(StateRecoveryPolicy.FenceAndDeactivate, manager.Options.RecoveryPolicy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fence_precedes_deactivation_and_survives_a_failed_deactivation_request(bool clear)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var canceled = new CancellationTokenSource();
        var failure = new OperationCanceledException(canceled.Token);
        var storage = new RecoveryStorage<Snapshot>(new("stored")) { MutationFailure = failure };
        var context = new RecoveryGrainContext(services);
        var manager = new DefaultStateManager<Snapshot>(storage, () => new("default"), grainContext: context);
        DeactivationReason? requestedReason = null;
        CancellationToken? requestedToken = null;
        Exception? failureDuringDeactivation = null;
        Snapshot? snapshotDuringDeactivation = null;
        context.OnDeactivate = (reason, token) =>
        {
            canceled.Cancel();
            requestedReason = reason;
            requestedToken = token;
            snapshotDuringDeactivation = manager.State;
            failureDuringDeactivation = Record.Exception(() => manager.State = new("rejected"));
            throw new InvalidOperationException("deactivation failed");
        };

        Assert.Same(failure, await Assert.ThrowsAsync<OperationCanceledException>(() => clear
            ? manager.ClearAsync(canceled.Token) : manager.WriteAsync(new("candidate"), canceled.Token)));

        await Assert.ThrowsAsync<StateManagerFencedException>(() => manager.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, context.Deactivations);
        Assert.Equal(DeactivationReasonCode.ApplicationError, requestedReason!.Value.ReasonCode);
        Assert.Same(failure, requestedReason.Value.Exception);
        Assert.False(requestedToken!.Value.CanBeCanceled);
        Assert.Equal("stored", snapshotDuringDeactivation!.Value);
        Assert.Same(failure, Assert.IsType<StateManagerFencedException>(failureDuringDeactivation).InnerException);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Failed_fence_recording_cannot_prevent_deactivation_or_replace_the_storage_failure(bool clear, bool readFailure)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var failure = new IOException("Storage failed.");
        var recordingFailure = new InvalidOperationException("Component storage unavailable.");
        var storage = new RecoveryStorage<Snapshot>(new("stored")) { MutationFailure = failure };
        var context = new RecoveryGrainContext(services)
        {
            ComponentReadFailure = readFailure ? recordingFailure : null,
            ComponentWriteFailure = readFailure ? null : recordingFailure,
        };
        DeactivationReason? reason = null;
        context.OnDeactivate = (value, _) => reason = value;
        var manager = new DefaultStateManager<Snapshot>(storage, () => new("default"), grainContext: context);

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => clear
            ? manager.ClearAsync(TestContext.Current.CancellationToken)
            : manager.WriteAsync(new("candidate"), TestContext.Current.CancellationToken)));

        Assert.Equal(1, context.Deactivations);
        Assert.Equal(DeactivationReasonCode.ApplicationError, reason!.Value.ReasonCode);
        Assert.Same(failure, reason.Value.Exception);
        Assert.Same(failure, (await Assert.ThrowsAsync<StateManagerFencedException>(
            () => manager.SaveChangesAsync(TestContext.Current.CancellationToken))).InnerException);
        Assert.Equal("stored", manager.State.Value);
        Assert.Equal(0, storage.Reads);
        Assert.Equal(1, storage.Mutations);
    }

    [Fact]
    public async Task Read_validation_and_pre_storage_cancellation_leave_the_manager_usable()
    {
        var storage = new RecoveryStorage<Snapshot>(new("stored")) { ReadFailure = new IOException() };
        var manager = new DefaultStateManager<Snapshot>(storage, () => new("default"));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAsync<IOException>(() => manager.ReadAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.WriteAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OperationCanceledException>(() => manager.WriteAsync(new("candidate"), canceled.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => manager.ClearAsync(canceled.Token));
        Assert.Throws<ArgumentNullException>(() => manager.ConfigureHooks(null!));

        Assert.Equal("stored", manager.State.Value);
        Assert.Equal(0, storage.Mutations);
        storage.ReadFailure = null;
        await manager.WriteAsync(new("next"), TestContext.Current.CancellationToken);
        Assert.Equal("next", manager.State.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task User_callback_failure_after_commit_does_not_fence(bool hook)
    {
        var storage = new RecoveryStorage<Snapshot>(new("stored"));
        var manager = new DefaultStateManager<Snapshot>(storage, () => new("default"), value =>
        {
            if (!hook && value.Value == "candidate") throw new InvalidOperationException("configuration failed");
        });
        manager.ConfigureHooks(hooks => hooks.OnWriteAsync = (_, _) =>
            hook ? throw new InvalidOperationException("hook failed") : Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.WriteAsync(new("candidate"), TestContext.Current.CancellationToken));

        Assert.Equal("candidate", manager.State.Value);
        Assert.False(manager.HasUnsavedChanges);
        Assert.Equal(0, storage.Reads);
        manager.ConfigureHooks(_ => { });
        await manager.WriteAsync(new("next"), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(StateRecoveryPolicy.ReadBack)]
    [InlineData(StateRecoveryPolicy.FenceAndDeactivate)]
    public async Task Both_policies_stamp_a_copy_of_versioned_state(StateRecoveryPolicy policy)
    {
        var storage = new RecoveryStorage<VersionedSnapshot>(new("stored"));
        var manager = new DefaultStateManager<VersionedSnapshot>(storage, () => new("default"), recoveryPolicy: policy);
        var candidate = new VersionedSnapshot("candidate");
        var originalVersion = candidate.Version;

        await manager.WriteAsync(candidate, TestContext.Current.CancellationToken);

        Assert.Equal(originalVersion, candidate.Version);
        Assert.NotEqual(originalVersion, manager.State.Version);
        Assert.Equal(manager.State.Version, storage.Persisted!.Version);
    }

    private sealed record Snapshot(string Value);
    private sealed record VersionedSnapshot(string Value) : VersionedState;
}

// This boundary keeps the durable record separate from the facet's mutable candidate,
// so losing a response after commit cannot be confused with rejecting the write.
internal sealed class RecoveryStorage<T>(T initial) : IPersistentState<T> where T : class
{
    public T State { get; set; } = initial;
    public T? Persisted { get; private set; } = initial;
    public string Etag => "etag";
    public bool RecordExists => Persisted is not null;
    public Exception? MutationFailure { get; set; }
    public Exception? ReadFailure { get; set; }
    public bool PersistBeforeFailure { get; set; }
    public int Reads { get; private set; }
    public int Mutations { get; private set; }

    public Task ReadStateAsync()
    {
        Reads++;
        if (ReadFailure is { } failure)
            throw failure;
        State = Persisted!;
        return Task.CompletedTask;
    }

    public Task WriteStateAsync() => Mutate(State);
    public Task ClearStateAsync() => Mutate(null);
    public Task ReadStateAsync(CancellationToken cancellationToken) => ReadStateAsync();
    public Task WriteStateAsync(CancellationToken cancellationToken) => WriteStateAsync();
    public Task ClearStateAsync(CancellationToken cancellationToken) => ClearStateAsync();

    private Task Mutate(T? next)
    {
        Mutations++;
        if (MutationFailure is null || PersistBeforeFailure)
            Persisted = next;
        if (MutationFailure is { } failure)
            throw failure;
        return Task.CompletedTask;
    }
}
