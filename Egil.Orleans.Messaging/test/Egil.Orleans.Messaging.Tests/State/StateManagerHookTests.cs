using Egil.Orleans.Messaging.Tests.Fakes;

namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerHookTests
{
    [Theory]
    [InlineData(StateManagerOperation.Read, true, "stored")]
    [InlineData(StateManagerOperation.Read, false, "default")]
    [InlineData(StateManagerOperation.Write, true, "next")]
    [InlineData(StateManagerOperation.Clear, false, "default")]
    public async Task Confirmed_operations_publish_and_configure_before_common_and_specific_handlers(
        StateManagerOperation operation, bool exists, string value)
    {
        var storage = new FakeHookPersistentState(operation == StateManagerOperation.Read && !exists ? null : new("stored"));
        var configured = false;
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"), _ => configured = true);
        var events = new List<string>();
        manager.ConfigureHooks(new()
        {
            OnChange = (state, kind, recordExists) =>
            {
                Assert.True(configured);
                Assert.Same(state, manager.State);
                Assert.Equal(operation, kind);
                Assert.Equal(exists, recordExists);
                events.Add($"common:{state.Value}");
            },
            OnRead = state => events.Add($"specific:{state.Value}"),
            OnWrite = state => events.Add($"specific:{state.Value}"),
            OnClear = state => events.Add($"specific:{state.Value}")
        });
        configured = false;

        await Invoke(manager, operation, TestContext.Current.CancellationToken);

        Assert.Equal([$"common:{value}", $"specific:{value}"], events);
    }

    [Theory]
    [InlineData(StateManagerOperation.Write, true, StateManagerOperation.Write)]
    [InlineData(StateManagerOperation.Clear, true, StateManagerOperation.Clear)]
    [InlineData(StateManagerOperation.Write, false, StateManagerOperation.Read)]
    [InlineData(StateManagerOperation.Clear, false, StateManagerOperation.Read)]
    public async Task Recovery_reports_only_the_confirmed_outcome(
        StateManagerOperation attempted, bool persisted, StateManagerOperation expected)
    {
        var storage = new FakeHookPersistentState(new("stored")) { MutationError = new IOException("lost"), PersistBeforeError = persisted };
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        var events = new List<StateManagerOperation>();
        manager.ConfigureHooks(new() { OnChange = (_, kind, _) => events.Add(kind) });

        var error = await Record.ExceptionAsync(() => Invoke(manager, attempted, TestContext.Current.CancellationToken));

        Assert.Equal(persisted ? null : storage.MutationError, error);
        Assert.Equal([expected], events);
        Assert.Equal(1, storage.Reads);
    }

    [Theory]
    [InlineData(StateManagerOperation.Write)]
    [InlineData(StateManagerOperation.Clear)]
    public async Task Handler_failures_preserve_durable_outcome_and_both_handlers_are_attempted(StateManagerOperation operation)
    {
        var storage = new FakeHookPersistentState(new("stored"));
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        var common = new InvalidOperationException("common");
        var specific = new IOException("specific");
        manager.ConfigureHooks(new()
        {
            OnChange = (_, _, _) => throw common,
            OnWrite = _ => throw specific,
            OnClear = _ => throw specific
        });

        var error = await Assert.ThrowsAsync<AggregateException>(() => Invoke(manager, operation, TestContext.Current.CancellationToken));

        Assert.Equal([common, specific], error.InnerExceptions);
        Assert.Equal(operation == StateManagerOperation.Write ? new("next") : null, storage.Persisted);
        Assert.Equal(operation == StateManagerOperation.Write ? "next" : "default", manager.State.Value);
        Assert.False(manager.HasUnsavedChanges);
        Assert.Equal(0, storage.Reads);
        Assert.Equal(1, storage.Mutations);
    }

    [Theory]
    [InlineData(StateManagerOperation.Write)]
    [InlineData(StateManagerOperation.Clear)]
    public async Task Mismatched_recovery_preserves_storage_error_before_handler_errors(StateManagerOperation operation)
    {
        var storage = new FakeHookPersistentState(new("stored")) { MutationError = new IOException("storage") };
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        var common = new InvalidOperationException("common");
        var specific = new IOException("specific");
        manager.ConfigureHooks(new() { OnChange = (_, _, _) => throw common, OnRead = _ => throw specific });

        var error = await Assert.ThrowsAsync<AggregateException>(() => Invoke(manager, operation, TestContext.Current.CancellationToken));

        Assert.Equal([storage.MutationError, common, specific], error.InnerExceptions);
        Assert.Equal("stored", manager.State.Value);
    }

    [Fact]
    public async Task Reconfiguration_during_storage_and_common_handler_cannot_split_an_operation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new FakeHookPersistentState(new("stored")) { Started = started, Release = release.Task };
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        var events = new List<string>();
        manager.ConfigureHooks(new()
        {
            OnChange = (_, _, _) => { events.Add("old common"); manager.ConfigureHooks(new()); },
            OnWrite = _ => events.Add("old write")
        });
        var pending = manager.WriteAsync(new("next"), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        manager.ConfigureHooks(new() { OnWrite = _ => events.Add("new write") });

        release.SetResult();
        await pending;
        await manager.WriteAsync(new("later"), TestContext.Current.CancellationToken);

        Assert.Equal(["old common", "old write"], events);
    }

    [Theory]
    [InlineData(StateManagerOperation.Write)]
    [InlineData(StateManagerOperation.Clear)]
    public async Task Cancellation_after_commit_still_invokes_and_awaits_handlers(StateManagerOperation operation)
    {
        using var cancellation = new CancellationTokenSource();
        var storage = new FakeHookPersistentState(new("stored")) { CancelAfterCommit = cancellation };
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ConfigureHooks(new()
        {
            OnChangeAsync = async (_, _, _, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                Assert.True(token.IsCancellationRequested);
                started.SetResult();
                await release.Task;
                token.ThrowIfCancellationRequested();
            }
        });
        var pending = Invoke(manager, operation, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);

        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        Assert.Equal(operation == StateManagerOperation.Write ? new("next") : null, storage.Persisted);
        Assert.Equal(0, storage.Reads);
    }

    [Fact]
    public async Task Handlers_can_read_state_but_cannot_reenter_any_storage_operation()
    {
        var storage = new FakeHookPersistentState(new("stored"));
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        var invoked = false;
        manager.ConfigureHooks(new()
        {
            OnReadAsync = async (state, _) =>
            {
                Assert.Same(state, manager.State);
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadAsync(TestContext.Current.CancellationToken));
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.WriteAsync(new("nested"), TestContext.Current.CancellationToken));
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ClearAsync(TestContext.Current.CancellationToken));
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveChangesAsync(TestContext.Current.CancellationToken));
                invoked = true;
            }
        });

        await manager.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(invoked);
        Assert.Equal(1, storage.Reads);
        Assert.Equal(0, storage.Mutations);
    }

    [Fact]
    public async Task Initial_notification_runs_once_without_reading_storage_and_reconfiguration_does_not_replay()
    {
        var storage = new FakeHookPersistentState(null);
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        var events = new List<string>();
        manager.ConfigureHooks(new() { OnRead = state => events.Add(state.Value) });

        await manager.InitializeAsync(TestContext.Current.CancellationToken);
        manager.ConfigureHooks(new() { OnRead = _ => events.Add("replacement") });
        await manager.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["default"], events);
        Assert.Equal(0, storage.Reads);
    }

    [Fact]
    public async Task Assignment_noop_save_and_failed_storage_without_adoption_do_not_notify()
    {
        var storage = new FakeHookPersistentState(new("stored")) { MutationError = new IOException("write"), ReadError = new IOException("read") };
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        manager.ConfigureHooks(new() { OnChange = (_, _, _) => Assert.Fail("Unexpected hook") });

        await manager.SaveChangesAsync(TestContext.Current.CancellationToken);
        manager.State = new("unsaved");
        await Assert.ThrowsAsync<IOException>(() => manager.WriteAsync(new("next"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<IOException>(() => manager.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Invalid_reconfiguration_keeps_previous_handlers()
    {
        var manager = new DefaultStateManager<HookSnapshot>(new FakeHookPersistentState(new("stored")), () => new("default"));
        var calls = 0;
        manager.ConfigureHooks(new() { OnRead = _ => calls++ });

        Assert.Throws<ArgumentException>(() => manager.ConfigureHooks(new() { OnRead = _ => { }, OnReadAsync = (_, _) => Task.CompletedTask }));
        await manager.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(StateManagerOperation.Write)]
    [InlineData(StateManagerOperation.Clear)]
    public async Task Classified_nonpersistence_skips_hooks(StateManagerOperation operation)
    {
        var storage = new FakeHookPersistentState(new("stored")) { MutationError = new IOException("rejected") };
        var manager = new RejectingManager(storage);
        manager.ConfigureHooks(new() { OnChange = (_, _, _) => Assert.Fail("Unexpected hook") });

        var error = await Assert.ThrowsAsync<IOException>(() => Invoke(manager, operation, TestContext.Current.CancellationToken));

        Assert.Same(storage.MutationError, error);
        Assert.Equal(0, storage.Reads);
    }

    [Theory]
    [InlineData(StateManagerOperation.Read)]
    [InlineData(StateManagerOperation.Write)]
    [InlineData(StateManagerOperation.Clear)]
    public async Task Failed_transient_configuration_keeps_adopted_state_and_skips_hooks(StateManagerOperation operation)
    {
        var storage = new FakeHookPersistentState(new("stored"));
        var fail = false;
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"), _ =>
        {
            if (fail) throw new InvalidOperationException("configuration");
        });
        manager.ConfigureHooks(new() { OnChange = (_, _, _) => Assert.Fail("Unexpected hook") });
        fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(manager, operation, TestContext.Current.CancellationToken));

        Assert.Equal(operation switch { StateManagerOperation.Read => "stored", StateManagerOperation.Write => "next", _ => "default" }, manager.State.Value);
        Assert.Equal(operation == StateManagerOperation.Read ? 1 : 0, storage.Reads);
    }

    [Theory]
    [InlineData(StateManagerOperation.Write)]
    [InlineData(StateManagerOperation.Clear)]
    public async Task A_conflict_remains_a_read_outcome_even_when_recovery_matches_the_attempt(StateManagerOperation operation)
    {
        var storage = new FakeHookPersistentState(new("stored"))
        {
            MutationError = new global::Orleans.Storage.InconsistentStateException("conflict"),
            PersistBeforeError = true
        };
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        var events = new List<StateManagerOperation>();
        manager.ConfigureHooks(new() { OnChange = (_, kind, _) => events.Add(kind) });

        await Assert.ThrowsAsync<global::Orleans.Storage.InconsistentStateException>(() => Invoke(manager, operation, TestContext.Current.CancellationToken));

        Assert.Equal([StateManagerOperation.Read], events);
    }

    [Fact]
    public async Task Save_changes_emits_write_once_and_unchanged_explicit_reads_always_emit_read()
    {
        var manager = new DefaultStateManager<HookSnapshot>(new FakeHookPersistentState(new("stored")), () => new("default"));
        var events = new List<StateManagerOperation>();
        manager.ConfigureHooks(new() { OnChange = (_, kind, _) => events.Add(kind) });

        manager.State = new("next");
        await manager.SaveChangesAsync(TestContext.Current.CancellationToken);
        await manager.SaveChangesAsync(TestContext.Current.CancellationToken);
        await manager.ReadAsync(TestContext.Current.CancellationToken);
        await manager.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal([StateManagerOperation.Write, StateManagerOperation.Read, StateManagerOperation.Read], events);
    }

    private sealed class RejectingManager(FakeHookPersistentState storage) : StateManagerBase<HookSnapshot>(storage, () => new("default"))
    {
        protected override StorageFailureKind ClassifyWriteFailure(Exception exception) => StorageFailureKind.DidNotPersist;
        protected override StorageFailureKind ClassifyClearFailure(Exception exception) => StorageFailureKind.DidNotPersist;
    }

    private static Task Invoke(IStateManager<HookSnapshot> manager, StateManagerOperation operation, CancellationToken token = default) => operation switch
    {
        StateManagerOperation.Read => manager.ReadAsync(token),
        StateManagerOperation.Write => manager.WriteAsync(new("next"), token),
        StateManagerOperation.Clear => manager.ClearAsync(token),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

}

