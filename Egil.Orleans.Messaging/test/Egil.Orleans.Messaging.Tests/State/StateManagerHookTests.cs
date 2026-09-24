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
        manager.ConfigureHooks(hooks =>
        {
            hooks.OnChange = (state, kind, recordExists) =>
            {
                Assert.True(configured);
                Assert.Same(state, manager.State);
                Assert.Equal(operation, kind);
                Assert.Equal(exists, recordExists);
                events.Add($"common:{state.Value}");
            };
            hooks.OnRead = state => events.Add($"specific:{state.Value}");
            hooks.OnWrite = state => events.Add($"specific:{state.Value}");
            hooks.OnClear = state => events.Add($"specific:{state.Value}");
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
        manager.ConfigureHooks(hooks => { hooks.OnChange = (_, kind, _) => events.Add(kind); });

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
        manager.ConfigureHooks(hooks =>
        {
            hooks.OnChange = (_, _, _) => throw common;
            hooks.OnWrite = _ => throw specific;
            hooks.OnClear = _ => throw specific;
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
        manager.ConfigureHooks(hooks => { hooks.OnChange = (_, _, _) => throw common; hooks.OnRead = _ => throw specific; });

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
        manager.ConfigureHooks(hooks =>
        {
            hooks.OnChange = (_, _, _) => { events.Add("old common"); manager.ConfigureHooks(_ => { }); };
            hooks.OnWrite = _ => events.Add("old write");
        });
        var pending = manager.WriteAsync(new("next"), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        manager.ConfigureHooks(hooks => { hooks.OnWrite = _ => events.Add("new write"); });

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
        manager.ConfigureHooks(hooks =>
        {
            hooks.OnChangeAsync = async (_, _, _, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                Assert.True(token.IsCancellationRequested);
                started.SetResult();
                await release.Task;
                token.ThrowIfCancellationRequested();
            };
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
        manager.ConfigureHooks(hooks =>
        {
            hooks.OnReadAsync = async (state, _) =>
            {
                Assert.Same(state, manager.State);
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadAsync(TestContext.Current.CancellationToken));
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.WriteAsync(new("nested"), TestContext.Current.CancellationToken));
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ClearAsync(TestContext.Current.CancellationToken));
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveChangesAsync(TestContext.Current.CancellationToken));
                invoked = true;
            };
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
        manager.ConfigureHooks(hooks => { hooks.OnRead = state => events.Add(state.Value); });

        await manager.InitializeAsync(TestContext.Current.CancellationToken);
        manager.ConfigureHooks(hooks => { hooks.OnRead = _ => events.Add("replacement"); });
        await manager.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["default"], events);
        Assert.Equal(0, storage.Reads);
    }

    [Fact]
    public async Task Assignment_noop_save_and_failed_storage_without_adoption_do_not_notify()
    {
        var storage = new FakeHookPersistentState(new("stored")) { MutationError = new IOException("write"), ReadError = new IOException("read") };
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        manager.ConfigureHooks(hooks => { hooks.OnChange = (_, _, _) => Assert.Fail("Unexpected hook"); });

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
        manager.ConfigureHooks(hooks => { hooks.OnRead = _ => calls++; });

        Assert.Throws<ArgumentException>(() => manager.ConfigureHooks(hooks => { hooks.OnRead = _ => { }; hooks.OnReadAsync = (_, _) => Task.CompletedTask; }));
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
        manager.ConfigureHooks(hooks => { hooks.OnChange = (_, _, _) => Assert.Fail("Unexpected hook"); });

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
        manager.ConfigureHooks(hooks => { hooks.OnChange = (_, _, _) => Assert.Fail("Unexpected hook"); });
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
        manager.ConfigureHooks(hooks => { hooks.OnChange = (_, kind, _) => events.Add(kind); });

        await Assert.ThrowsAsync<global::Orleans.Storage.InconsistentStateException>(() => Invoke(manager, operation, TestContext.Current.CancellationToken));

        Assert.Equal([StateManagerOperation.Read], events);
    }

    [Fact]
    public async Task Save_changes_emits_write_once_and_unchanged_explicit_reads_always_emit_read()
    {
        var manager = new DefaultStateManager<HookSnapshot>(new FakeHookPersistentState(new("stored")), () => new("default"));
        var events = new List<StateManagerOperation>();
        manager.ConfigureHooks(hooks => { hooks.OnChange = (_, kind, _) => events.Add(kind); });

        manager.State = new("next");
        await manager.SaveChangesAsync(TestContext.Current.CancellationToken);
        await manager.SaveChangesAsync(TestContext.Current.CancellationToken);
        await manager.ReadAsync(TestContext.Current.CancellationToken);
        await manager.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal([StateManagerOperation.Write, StateManagerOperation.Read, StateManagerOperation.Read], events);
    }

    [Fact]
    public async Task Completing_an_overlapping_dispatch_does_not_allow_nested_operations_in_a_suspended_handler()
    {
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var storage = new FakeHookPersistentState(new("stored"))
        {
            BeforeRead = () => ++reads == 1 ? firstRead.Task : secondRead.Task
        };
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        var handlers = 0;
        manager.ConfigureHooks(hooks =>
        {
            hooks.OnReadAsync = async (_, token) =>
            {
                if (++handlers != 1) return;
                handlerStarted.SetResult();
                await releaseHandler.Task.WaitAsync(token);
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadAsync(token));
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.WriteAsync(new("nested"), token));
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ClearAsync(token));
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveChangesAsync(token));
            };
        });
        var first = manager.ReadAsync(TestContext.Current.CancellationToken);
        var second = manager.ReadAsync(TestContext.Current.CancellationToken);
        firstRead.SetResult();
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        secondRead.SetResult();
        await second;
        releaseHandler.SetResult();
        await first;

        Assert.Equal(2, storage.Reads);
        Assert.Equal(0, storage.Mutations);
        manager.ConfigureHooks(_ => { });
        await manager.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, storage.Reads);
    }

    [Theory]
    [InlineData(StateManagerOperation.Write)]
    [InlineData(StateManagerOperation.Clear)]
    public async Task Async_specific_handlers_are_awaited_and_fail_without_retrying_durable_storage(StateManagerOperation operation)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new FakeHookPersistentState(new("stored"));
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        var failure = new IOException("async specific handler failed");
        async Task Handle(HookSnapshot state, CancellationToken token)
        {
            Assert.Same(state, manager.State);
            Assert.Equal(TestContext.Current.CancellationToken, token);
            started.SetResult();
            await release.Task.WaitAsync(token);
            throw failure;
        }
        manager.ConfigureHooks(hooks => { hooks.OnWriteAsync = Handle; hooks.OnClearAsync = Handle; });
        var pending = Invoke(manager, operation, TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);

        release.SetResult();
        var error = await Assert.ThrowsAsync<IOException>(() => pending);

        Assert.Same(failure, error);
        Assert.Equal(operation == StateManagerOperation.Write ? new("next") : null, storage.Persisted);
        Assert.Equal(1, storage.Mutations);
        Assert.Equal(0, storage.Reads);
    }

    [Fact]
    public async Task An_independent_read_is_not_rejected_while_another_flows_handler_is_suspended()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new FakeHookPersistentState(new("stored"));
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        var handlers = 0;
        manager.ConfigureHooks(hooks =>
        {
            hooks.OnReadAsync = async (_, token) =>
            {
                if (++handlers != 1) return;
                started.SetResult();
                await release.Task.WaitAsync(token);
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadAsync(token));
            };
        });
        var first = manager.ReadAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var independentError = await Record.ExceptionAsync(() => manager.ReadAsync(TestContext.Current.CancellationToken));
        release.SetResult();
        await first;

        Assert.Null(independentError);
        Assert.Equal(2, storage.Reads);
        Assert.Equal(2, handlers);
    }

    [Fact]
    public async Task Retaining_the_configuration_object_cannot_change_installed_or_in_flight_handlers()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new FakeHookPersistentState(new("stored")) { Started = started, Release = release.Task };
        var manager = new DefaultStateManager<HookSnapshot>(storage, () => new("default"));
        StateManagerHooks<HookSnapshot>? retained = null;
        var events = new List<string>();
        var configurations = 0;
        manager.ConfigureHooks(hooks =>
        {
            configurations++;
            retained = hooks;
            hooks.OnWrite = _ => events.Add("installed");
        });
        Assert.NotNull(retained);
        var pending = manager.WriteAsync(new("next"), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        retained.OnWrite = _ => events.Add("mutated");
        release.SetResult();
        await pending;
        await manager.WriteAsync(new("later"), TestContext.Current.CancellationToken);

        Assert.Equal(["installed", "installed"], events);
        Assert.Equal(1, configurations);
    }

    [Fact]
    public async Task A_throwing_configuration_callback_leaves_all_previous_hooks_installed()
    {
        var manager = new DefaultStateManager<HookSnapshot>(new FakeHookPersistentState(new("stored")), () => new("default"));
        var events = new List<string>();
        var failure = new InvalidOperationException("configuration failed");
        manager.ConfigureHooks(hooks =>
        {
            hooks.OnChange = (_, _, _) => events.Add("common");
            hooks.OnRead = _ => events.Add("read");
        });

        var error = Assert.Throws<InvalidOperationException>(() => manager.ConfigureHooks(hooks =>
        {
            hooks.OnRead = _ => events.Add("partial");
            throw failure;
        }));
        await manager.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Same(failure, error);
        Assert.Equal(["common", "read"], events);
    }

    [Fact]
    public void Hook_configuration_cannot_be_constructed_or_derived_by_consumers()
    {
        Assert.Empty(typeof(StateManagerHooks<HookSnapshot>).GetConstructors());
        Assert.True(typeof(StateManagerHooks<HookSnapshot>).IsSealed);
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

