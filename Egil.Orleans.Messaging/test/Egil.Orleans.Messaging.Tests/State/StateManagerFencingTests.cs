namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerFencingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Failed_mutation_fences_without_reconciling_even_when_it_committed(bool clear, bool persisted)
    {
        var failure = new IOException("The storage response was lost.");
        var storage = new RecoveryStorage<Snapshot>(new("stored"))
        {
            MutationFailure = failure,
            PersistBeforeFailure = persisted,
        };
        IStateManager<Snapshot> manager = new DefaultStateManager<Snapshot>(storage, static () => new("default"));

        var error = await Assert.ThrowsAsync<IOException>(() => clear
            ? manager.ClearAsync(TestContext.Current.CancellationToken)
            : manager.WriteAsync(new("candidate"), TestContext.Current.CancellationToken));

        Assert.Same(failure, error);
        Assert.Equal(0, storage.Reads);
        var fenced = Assert.Throws<InvalidOperationException>(() => manager.State);
        Assert.Same(failure, fenced.InnerException);
        Assert.Contains("fenced", fenced.Message, StringComparison.Ordinal);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.State = null!).InnerException);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.HasUnsavedChanges).InnerException);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.ConfigureHooks(null!)).InnerException);
        var callbackRan = false;
        Assert.Throws<InvalidOperationException>(() => manager.ConfigureHooks(_ => callbackRan = true));
        Assert.False(callbackRan);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        Assert.Same(failure, (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadAsync(canceled.Token))).InnerException);
        Assert.Same(failure, (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ClearAsync(canceled.Token))).InnerException);
        Assert.Same(failure, (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.WriteAsync(null!, canceled.Token))).InnerException);
        Assert.Same(failure, (await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveChangesAsync(canceled.Token))).InnerException);
        Assert.Equal(1, storage.Mutations);
        Assert.Equal(0, storage.Reads);
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
        context.OnDeactivate = (reason, token) =>
        {
            canceled.Cancel();
            requestedReason = reason;
            requestedToken = token;
            failureDuringDeactivation = Record.Exception(() => manager.State);
            throw new InvalidOperationException("deactivation failed");
        };

        Assert.Same(failure, await Assert.ThrowsAsync<OperationCanceledException>(() => clear
            ? manager.ClearAsync(canceled.Token) : manager.WriteAsync(new("candidate"), canceled.Token)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, context.Deactivations);
        Assert.Equal(DeactivationReasonCode.ApplicationError, requestedReason!.Value.ReasonCode);
        Assert.Same(failure, requestedReason.Value.Exception);
        Assert.False(requestedToken!.Value.CanBeCanceled);
        Assert.Same(failure, Assert.IsType<InvalidOperationException>(failureDuringDeactivation).InnerException);
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
