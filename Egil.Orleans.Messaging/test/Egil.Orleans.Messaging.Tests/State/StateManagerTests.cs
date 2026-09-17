using Orleans.Storage;

namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerTests
{
    [Fact]
    public void Missing_state_exposes_default_without_writing()
    {
        var storage = new FakePersistentState(null);
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        Assert.Equal(new TestState("default"), manager.State);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void Missing_record_uses_configured_default_instead_of_provider_default()
    {
        var initial = new TestState("provider-default");
        var storage = new FakePersistentState(initial)
        {
            RecordExists = false
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        Assert.Equal(new TestState("default"), manager.State);
        Assert.False(storage.RecordExists);
    }

    [Fact]
    public async Task ReadAsync_refreshes_state_from_storage()
    {
        var storage = new FakePersistentState(new TestState("initial"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        storage.State = new TestState("fresh");

        await manager.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new TestState("fresh"), manager.State);
    }

    [Fact]
    public async Task ReadAsync_when_record_is_missing_exposes_default()
    {
        var storage = new FakePersistentState(new TestState("initial"))
        {
            OnRead = state =>
            {
                state.State = null!;
                state.RecordExists = false;
            }
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        await manager.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new TestState("default"), manager.State);
        Assert.Equal(0, storage.WriteCount);
        Assert.False(storage.RecordExists);
    }

    [Fact]
    public async Task WriteAsync_success_updates_committed_state()
    {
        var storage = new FakePersistentState(new TestState("initial"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        var next = new TestState("next");

        await manager.WriteAsync(next, TestContext.Current.CancellationToken);

        Assert.Equal(next, manager.State);
        Assert.Equal(next, storage.State);
    }

    [Fact]
    public async Task WriteAsync_when_write_fails_but_read_shows_attempted_state_swallows_exception()
    {
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = new TimeoutException("write timeout"),
            OnRead = state => state.State = new TestState("next")
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        var next = new TestState("next");

        await manager.WriteAsync(next, TestContext.Current.CancellationToken);

        Assert.Equal(next, manager.State);
    }

    [Fact]
    public async Task WriteAsync_when_write_fails_and_read_shows_different_state_adopts_persisted_state_and_rethrows()
    {
        var writeException = new TimeoutException("write timeout");
        var persisted = new TestState("other");
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = writeException,
            OnRead = state =>
            {
                state.State = persisted;
                state.Etag = "etag-2";
            }
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => manager.WriteAsync(new TestState("next"), TestContext.Current.CancellationToken));

        Assert.Same(writeException, ex);
        Assert.Equal(persisted, manager.State);
        Assert.Equal(persisted, storage.State);
        Assert.Equal("etag-2", storage.Etag);
    }

    [Fact]
    public async Task WriteAsync_on_concurrency_conflict_adopts_persisted_state_and_rethrows()
    {
        var writeException = new InconsistentStateException("write conflict");
        var attempted = new TestState("next");
        var persisted = new TestState("other");
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = writeException,
            OnRead = state =>
            {
                state.State = persisted;
                state.Etag = "etag-2";
            }
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        var ex = await Assert.ThrowsAsync<InconsistentStateException>(
            () => manager.WriteAsync(attempted, TestContext.Current.CancellationToken));

        Assert.Same(writeException, ex);
        Assert.Equal(persisted, manager.State);
        Assert.Equal(persisted, storage.State);
        Assert.Equal("etag-2", storage.Etag);
    }

    [Fact]
    public async Task WriteAsync_on_concurrency_conflict_rethrows_when_persisted_state_matches_attempt()
    {
        var writeException = new InconsistentStateException("write conflict");
        var attempted = new TestState("next");
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = writeException,
            OnRead = state =>
            {
                state.State = new TestState("next");
                state.Etag = "etag-2";
            }
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        var ex = await Assert.ThrowsAsync<InconsistentStateException>(
            () => manager.WriteAsync(attempted, TestContext.Current.CancellationToken));

        Assert.Same(writeException, ex);
        Assert.Equal(new TestState("next"), manager.State);
        Assert.Equal("etag-2", storage.Etag);
    }

    [Fact]
    public async Task WriteAsync_when_write_and_read_fail_reverts_state_and_rethrows_original_exception()
    {
        var writeException = new TimeoutException("write timeout");
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = writeException,
            ReadException = new InvalidOperationException("read failed")
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => manager.WriteAsync(new TestState("next"), TestContext.Current.CancellationToken));

        Assert.Same(writeException, ex);
        Assert.Equal(new TestState("initial"), manager.State);
    }

    [Fact]
    public async Task WriteAsync_when_conflict_and_read_fail_reverts_state_and_rethrows_conflict()
    {
        var initial = new TestState("initial");
        var writeException = new InconsistentStateException("write conflict");
        var storage = new FakePersistentState(initial)
        {
            WriteException = writeException,
            ReadException = new InvalidOperationException("read failed")
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        var ex = await Assert.ThrowsAsync<InconsistentStateException>(
            () => manager.WriteAsync(new TestState("next"), TestContext.Current.CancellationToken));

        Assert.Same(writeException, ex);
        Assert.Same(initial, manager.State);
        Assert.Same(initial, storage.State);
    }

    [Fact]
    public async Task WriteAsync_on_versioned_state_stamps_new_version()
    {
        var original = new VersionedTestState("initial");
        var storage = new FakePersistentVersionedState(original);
        var manager = new DefaultStateManager<VersionedTestState>(storage, static () => new("default"));
        var next = new VersionedTestState("next") { Version = Guid.Empty };

        await manager.WriteAsync(next, TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, next.Version);
        Assert.Equal(next.Version, manager.State!.Version);
    }

    [Fact]
    public async Task ClearAsync_success_updates_committed_state()
    {
        var storage = new FakePersistentState(new TestState("initial"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        await manager.ClearAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new TestState("default"), manager.State);
        Assert.Equal(0, storage.WriteCount);
        Assert.False(storage.RecordExists);
    }

    [Fact]
    public async Task WriteAsync_after_clear_can_persist_new_state()
    {
        var storage = new FakePersistentState(new TestState("initial"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        var next = new TestState("next");

        await manager.ClearAsync(TestContext.Current.CancellationToken);
        await manager.WriteAsync(next, TestContext.Current.CancellationToken);

        Assert.Equal(next, manager.State);
        Assert.Equal(next, storage.State);
    }

    [Fact]
    public async Task WriteAsync_from_cleared_state_when_recovery_read_is_missing_keeps_default()
    {
        var writeException = new TimeoutException("write timeout");
        var storage = new FakePersistentState(new TestState("initial"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        await manager.ClearAsync(TestContext.Current.CancellationToken);
        storage.WriteException = writeException;
        storage.OnRead = state =>
        {
            state.State = null!;
            state.RecordExists = false;
        };

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => manager.WriteAsync(new TestState("next"), TestContext.Current.CancellationToken));

        Assert.Same(writeException, ex);
        Assert.Equal(new TestState("default"), manager.State);
        Assert.Equal(1, storage.WriteCount);
        Assert.False(storage.RecordExists);
    }

    [Fact]
    public async Task ClearAsync_when_clear_fails_but_read_shows_missing_record_swallows_exception()
    {
        var storage = new FakePersistentState(new TestState("initial"))
        {
            ClearException = new TimeoutException("clear timeout"),
            OnRead = state =>
            {
                state.State = null!;
                state.RecordExists = false;
            }
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        await manager.ClearAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new TestState("default"), manager.State);
        Assert.Equal(0, storage.WriteCount);
        Assert.False(storage.RecordExists);
    }

    [Fact]
    public async Task ClearAsync_when_clear_fails_and_read_shows_record_still_exists_adopts_state_and_rethrows()
    {
        var clearException = new TimeoutException("clear timeout");
        var persisted = new TestState("persisted");
        var storage = new FakePersistentState(new TestState("initial"))
        {
            ClearException = clearException,
            OnRead = state =>
            {
                state.State = persisted;
                state.RecordExists = true;
                state.Etag = "etag-2";
            }
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => manager.ClearAsync(TestContext.Current.CancellationToken));

        Assert.Same(clearException, ex);
        Assert.Same(persisted, manager.State);
        Assert.True(storage.RecordExists);
        Assert.Equal("etag-2", storage.Etag);
    }

    [Fact]
    public async Task ClearAsync_on_conflict_when_read_shows_missing_record_adopts_state_and_rethrows_conflict()
    {
        var clearException = new InconsistentStateException("clear conflict");
        var storage = new FakePersistentState(new TestState("initial"))
        {
            ClearException = clearException,
            OnRead = state =>
            {
                state.State = null!;
                state.RecordExists = false;
            }
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        var ex = await Assert.ThrowsAsync<InconsistentStateException>(() => manager.ClearAsync(TestContext.Current.CancellationToken));

        Assert.Same(clearException, ex);
        Assert.Equal(new TestState("default"), manager.State);
        Assert.Equal(0, storage.WriteCount);
        Assert.False(storage.RecordExists);
    }

    [Fact]
    public async Task ClearAsync_when_clear_and_read_fail_reverts_state_and_rethrows_original_exception()
    {
        var clearException = new TimeoutException("clear timeout");
        var storage = new FakePersistentState(new TestState("initial"))
        {
            ClearException = clearException,
            ReadException = new InvalidOperationException("read failed")
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => manager.ClearAsync(TestContext.Current.CancellationToken));

        Assert.Same(clearException, ex);
        Assert.Equal(new TestState("initial"), manager.State);
    }

    [Fact]
    public async Task Missing_record_does_not_confirm_an_ambiguous_write_equal_to_the_default()
    {
        var failure = new TimeoutException("lost write");
        var storage = new FakePersistentState(null)
        {
            WriteException = failure,
            OnRead = loaded => { loaded.State = null!; loaded.RecordExists = false; }
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        var error = await Assert.ThrowsAsync<TimeoutException>(() => manager.WriteAsync(new("default"), TestContext.Current.CancellationToken));

        Assert.Same(failure, error);
        Assert.Equal(new TestState("default"), manager.State);
        Assert.False(storage.RecordExists);
    }

    [Fact]
    public async Task Runtime_configuration_follows_loaded_and_default_instances()
    {
        var configured = new List<TestState>();
        var initial = new TestState("initial");
        var loaded = new TestState("loaded");
        var storage = new FakePersistentState(initial) { OnRead = state => state.State = loaded };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"), configured.Add);

        await manager.ReadAsync(TestContext.Current.CancellationToken);
        await manager.ClearAsync(TestContext.Current.CancellationToken);

        Assert.Equal([initial, loaded, new TestState("default")], configured);
        Assert.Same(configured[2], manager.State);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void Null_factory_result_is_rejected()
    {
        var storage = new FakePersistentState(null);

        var error = Assert.Throws<InvalidOperationException>(() =>
            new DefaultStateManager<TestState>(storage, static () => null!));

        Assert.Contains("factory returned null", error.Message);
    }

    [Fact]
    public void Existing_null_record_is_not_replaced_with_defaults()
    {
        var storage = new FakePersistentState(null) { RecordExists = true };

        var error = Assert.Throws<InvalidOperationException>(() =>
            new DefaultStateManager<TestState>(storage, static () => new("default")));

        Assert.Contains("existing state record", error.Message);
    }

    [Fact]
    public async Task Readers_keep_the_previous_snapshot_until_the_write_completes()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = new TestState("previous");
        var next = new TestState("next");
        var storage = new FakePersistentState(previous) { WriteCompletion = completion.Task };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        var write = manager.WriteAsync(next, TestContext.Current.CancellationToken);

        Assert.Same(previous, manager.State);
        Assert.Same(next, storage.State);
        completion.SetResult();
        await write;
        Assert.Same(next, manager.State);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("clear")]
    [InlineData("write-recovery")]
    [InlineData("clear-recovery")]
    public async Task Configuration_observes_the_adopted_snapshot(string operation)
    {
        var storage = new FakePersistentState(new TestState("initial"));
        DefaultStateManager<TestState>? manager = null;
        TestState? observedManager = null;
        TestState? observedStorage = null;
        TestState? configured = null;
        manager = new DefaultStateManager<TestState>(storage, static () => new("default"), value =>
        {
            configured = value;
            observedManager = manager?.State;
            observedStorage = storage.State;
        });
        storage.OnRead = loaded => loaded.State = new("loaded");
        storage.WriteException = operation == "write-recovery" ? new TimeoutException() : null;
        storage.ClearException = operation == "clear-recovery" ? new TimeoutException() : null;

        _ = await Record.ExceptionAsync(() => operation switch
        {
            "read" => manager.ReadAsync(TestContext.Current.CancellationToken),
            "write" or "write-recovery" => manager.WriteAsync(new("next"), TestContext.Current.CancellationToken),
            _ => manager.ClearAsync(TestContext.Current.CancellationToken)
        });

        Assert.Same(configured, observedManager);
        Assert.Same(configured, observedStorage);
        Assert.Same(configured, manager.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configuration_failure_after_successful_storage_is_not_retried_or_hidden(bool clear)
    {
        var storage = new FakePersistentState(new TestState("initial"));
        var failure = new InvalidOperationException("runtime configuration failed");
        var failNextConfiguration = false;
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"), _ =>
        {
            if (failNextConfiguration)
            {
                failNextConfiguration = false;
                throw failure;
            }
        });
        failNextConfiguration = true;
        var recoveryReads = 0;
        storage.OnRead = _ => recoveryReads++;

        var error = await Record.ExceptionAsync(() => clear ? manager.ClearAsync(TestContext.Current.CancellationToken) : manager.WriteAsync(new("next"), TestContext.Current.CancellationToken));

        Assert.Same(failure, error);
        Assert.Equal(clear ? "default" : "next", manager.State.Value);
        Assert.Equal(0, recoveryReads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Invalid_state_after_recovery_read_is_reported_as_validation_failure(bool clear, bool recordExists)
    {
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = new TimeoutException("write failed"),
            ClearException = new TimeoutException("clear failed"),
            OnRead = loaded => { loaded.State = null!; loaded.RecordExists = recordExists; }
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => null!);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => clear ? manager.ClearAsync(TestContext.Current.CancellationToken) : manager.WriteAsync(new("next"), TestContext.Current.CancellationToken));

        Assert.Contains(recordExists ? "existing state record" : "factory returned null", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configuration_failure_after_recovery_is_reported_and_keeps_loaded_state(bool clear)
    {
        var loaded = new TestState("loaded");
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = new TimeoutException(),
            ClearException = new TimeoutException(),
            OnRead = state => state.State = loaded
        };
        var failure = new InvalidOperationException("runtime configuration failed");
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"), value =>
        {
            if (ReferenceEquals(value, loaded)) throw failure;
        });

        var error = await Record.ExceptionAsync(() => clear ? manager.ClearAsync(TestContext.Current.CancellationToken) : manager.WriteAsync(new("next"), TestContext.Current.CancellationToken));

        Assert.Same(failure, error);
        Assert.Same(loaded, manager.State);
        Assert.Same(loaded, storage.State);
    }

    [Fact]
    public void Assigning_State_publishes_the_snapshot_without_writing_to_storage()
    {
        var storage = new FakePersistentState(new TestState("stored"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        var staged = new TestState("staged");

        manager.State = staged;

        Assert.Same(staged, manager.State);
        Assert.Same(staged, storage.State);
        Assert.True(manager.HasUnsavedChanges);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public void Assigning_null_to_State_is_rejected()
    {
        var storage = new FakePersistentState(new TestState("stored"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        Assert.Throws<ArgumentNullException>(() => manager.State = null!);
    }

    [Fact]
    public void Assigning_State_runs_runtime_configuration_on_the_new_instance()
    {
        var configured = new List<TestState>();
        var storage = new FakePersistentState(new TestState("stored"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"), configured.Add);
        var staged = new TestState("staged");

        manager.State = staged;

        Assert.Same(staged, configured[^1]);
    }

    [Fact]
    public async Task SaveChangesAsync_persists_the_current_snapshot_once()
    {
        var storage = new FakePersistentState(new TestState("stored"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        manager.State = new TestState("first");
        var last = new TestState("last");
        manager.State = last;

        await manager.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Same(last, manager.State);
        Assert.Same(last, storage.State);
        Assert.False(manager.HasUnsavedChanges);
        Assert.Equal(1, storage.WriteCount);
    }

    [Fact]
    public async Task SaveChangesAsync_without_unsaved_changes_does_not_reach_storage_or_observe_cancellation()
    {
        var storage = new FakePersistentState(new TestState("stored"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await manager.SaveChangesAsync(cancellation.Token);

        Assert.Equal(0, storage.WriteCount);
        Assert.Equal(new TestState("stored"), manager.State);
    }

    [Fact]
    public async Task WriteAsync_with_a_new_state_supersedes_the_unsaved_snapshot()
    {
        var storage = new FakePersistentState(new TestState("stored"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        manager.State = new TestState("staged");
        var next = new TestState("next");

        await manager.WriteAsync(next, TestContext.Current.CancellationToken);

        Assert.Same(next, manager.State);
        Assert.False(manager.HasUnsavedChanges);
        Assert.Equal(1, storage.WriteCount);
    }

    [Fact]
    public async Task Assigning_State_does_not_stamp_a_version_but_the_following_write_does()
    {
        var storage = new FakePersistentVersionedState(new VersionedTestState("stored"));
        var manager = new DefaultStateManager<VersionedTestState>(storage, static () => new("default"));
        var staged = new VersionedTestState("staged") { Version = Guid.Empty };

        manager.State = staged;

        Assert.Equal(Guid.Empty, staged.Version);

        await manager.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, staged.Version);
    }

    [Fact]
    public async Task Write_that_definitely_did_not_persist_discards_the_unsaved_snapshot()
    {
        var stored = new TestState("stored");
        var storage = new FakePersistentState(stored) { WriteException = new TimeoutException("write rejected") };
        var manager = new RejectingStateManager(storage, static () => new("default"));
        manager.State = new TestState("staged");

        await Assert.ThrowsAsync<TimeoutException>(() => manager.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Same(stored, manager.State);
        Assert.Same(stored, storage.State);
        Assert.False(manager.HasUnsavedChanges);
    }

    [Fact]
    public async Task Repeated_assignment_does_not_drift_the_value_a_failed_write_reverts_to()
    {
        var stored = new TestState("stored");
        var storage = new FakePersistentState(stored);
        var manager = new RejectingStateManager(storage, static () => new("default"));
        manager.State = new TestState("first");
        manager.State = new TestState("second");
        storage.WriteException = new TimeoutException("write rejected");

        await Assert.ThrowsAsync<TimeoutException>(() => manager.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Same(stored, manager.State);
        Assert.False(manager.HasUnsavedChanges);
    }

    [Fact]
    public async Task Ambiguous_write_confirmed_by_read_back_clears_the_unsaved_marker()
    {
        var staged = new TestState("staged");
        var storage = new FakePersistentState(new TestState("stored"))
        {
            WriteException = new TimeoutException("write timeout"),
            OnRead = state => state.State = new TestState("staged")
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        manager.State = staged;

        await manager.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(staged, manager.State);
        Assert.False(manager.HasUnsavedChanges);
    }

    [Fact]
    public async Task Ambiguous_write_contradicted_by_read_back_adopts_storage_and_clears_the_unsaved_marker()
    {
        var persisted = new TestState("persisted");
        var storage = new FakePersistentState(new TestState("stored"))
        {
            WriteException = new TimeoutException("write timeout"),
            OnRead = state => state.State = persisted
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        manager.State = new TestState("staged");

        await Assert.ThrowsAsync<TimeoutException>(() => manager.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Same(persisted, manager.State);
        Assert.False(manager.HasUnsavedChanges);
    }

    [Fact]
    public async Task Write_that_fails_together_with_its_recovery_read_discards_the_unsaved_snapshot()
    {
        var stored = new TestState("stored");
        var storage = new FakePersistentState(stored)
        {
            WriteException = new TimeoutException("write timeout"),
            ReadException = new InvalidOperationException("read failed")
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        manager.State = new TestState("staged");

        await Assert.ThrowsAsync<TimeoutException>(() => manager.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Same(stored, manager.State);
        Assert.Same(stored, storage.State);
        Assert.False(manager.HasUnsavedChanges);
    }

    [Fact]
    public async Task ReadAsync_discards_unsaved_changes()
    {
        // Assignment mirrors into the facet, so the fake must replay what storage holds on
        // read the way a real provider does, instead of returning the staged value.
        var storage = new FakePersistentState(new TestState("stored"))
        {
            OnRead = state => state.State = new TestState("stored")
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        manager.State = new TestState("staged");

        await manager.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new TestState("stored"), manager.State);
        Assert.False(manager.HasUnsavedChanges);
    }

    [Fact]
    public async Task ClearAsync_discards_unsaved_changes()
    {
        var storage = new FakePersistentState(new TestState("stored"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        manager.State = new TestState("staged");

        await manager.ClearAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new TestState("default"), manager.State);
        Assert.False(manager.HasUnsavedChanges);
    }

    [Fact]
    public async Task Clear_whose_default_factory_fails_still_clears_the_unsaved_marker()
    {
        var storage = new FakePersistentState(new TestState("stored"));
        var failFactory = false;
        var manager = new DefaultStateManager<TestState>(
            storage,
            () => failFactory ? throw new InvalidOperationException("factory failed") : new TestState("default"));
        manager.State = new TestState("staged");
        failFactory = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ClearAsync(TestContext.Current.CancellationToken));

        // Storage was deleted before the factory ran. If the marker survived, the
        // documented deactivation flush would write the staged value straight back over
        // the record the grain just asked to delete.
        Assert.False(manager.HasUnsavedChanges);

        await manager.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public async Task Assignment_during_an_in_flight_write_loses_to_the_write_it_interleaved_with()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = new TestState("next");
        var storage = new FakePersistentState(new TestState("stored")) { WriteCompletion = completion.Task };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        var write = manager.WriteAsync(next, TestContext.Current.CancellationToken);
        manager.State = new TestState("staged");
        completion.SetResult();
        await write;

        // Only reachable in a [Reentrant] grain or with interleaved acknowledgement. The
        // staged value never reached storage, so adopting it would mark a value storage
        // never saw as durable and lose this write's fence. Discarding it costs one
        // redelivery, which the next post run corrects.
        Assert.Same(next, manager.State);
        Assert.Same(next, storage.State);
        Assert.False(manager.HasUnsavedChanges);

        // The durable value, not just the fence. Assignment mirrors into the facet, and the
        // facet is the same GrainState the provider still holds: replacing the value under
        // an unfinished write would persist the staged snapshot in place of this write.
        Assert.Same(next, storage.Persisted);
    }

    [Fact]
    public async Task Read_that_cannot_resolve_its_loaded_state_still_clears_the_unsaved_marker()
    {
        var storage = new FakePersistentState(new TestState("stored"))
        {
            OnRead = state => state.State = null!
        };
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        manager.State = new TestState("staged");

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadAsync(TestContext.Current.CancellationToken));

        // Storage answered, so the staged question is settled even though resolving the
        // value failed. A surviving marker would let a later flush write the staged value
        // back on top of the state that was just read.
        Assert.False(manager.HasUnsavedChanges);

        // And the staged value must not stay visible either: with the marker cleared it
        // would be indistinguishable from durable state.
        Assert.Equal(new TestState("stored"), manager.State);

        await manager.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, storage.WriteCount);
    }

    private sealed record TestState(string Value);

    private sealed record VersionedTestState(string Value) : VersionedState, IEquatable<VersionedTestState>;

    // DefaultStateManager classifies every write failure as UnknownOutcome, which routes
    // through read-back recovery. The DidNotPersist branch skips that, so it needs a
    // manager that reports it.
    private sealed class RejectingStateManager(IPersistentState<TestState> storage, Func<TestState> createInitialState)
        : StateManagerBase<TestState>(storage, createInitialState)
    {
        protected override StorageFailureKind ClassifyWriteFailure(Exception exception) => StorageFailureKind.DidNotPersist;

        protected override StorageFailureKind ClassifyClearFailure(Exception exception) => StorageFailureKind.DidNotPersist;
    }

    private sealed class FakePersistentState : IPersistentState<TestState>
    {
        public FakePersistentState(TestState? state)
        {
            State = state!;
            RecordExists = state is not null;
        }

        public Exception? ReadException { get; set; }
        public Exception? WriteException { get; set; }
        public Exception? ClearException { get; set; }
        public Task? WriteCompletion { get; init; }
        public int WriteCount { get; private set; }
        public TestState? Persisted { get; private set; }
        public Action<FakePersistentState>? OnRead { get; set; }
        public Action<FakePersistentState>? OnWrite { get; set; }
        public Action<FakePersistentState>? OnClear { get; set; }

        public string Etag { get; set; } = "etag-1";
        public bool RecordExists { get; set; } = true;
        public TestState State { get; set; }

        public Task ReadStateAsync()
        {
            OnRead?.Invoke(this);
            if (ReadException is not null)
            {
                throw ReadException;
            }

            return Task.CompletedTask;
        }

        public Task WriteStateAsync()
        {
            WriteCount++;
            OnWrite?.Invoke(this);
            if (WriteException is not null)
            {
                throw WriteException;
            }

            if (WriteCompletion is null)
            {
                Persisted = State;
                return Task.CompletedTask;
            }

            return CompleteAsync();

            async Task CompleteAsync()
            {
                await WriteCompletion;

                // A provider that serializes its GrainState after its first await sees
                // whatever is in the facet by then, not what was there at the call.
                Persisted = State;
            }
        }

        public Task ClearStateAsync()
        {
            OnClear?.Invoke(this);
            if (ClearException is not null)
            {
                throw ClearException;
            }

            State = null!;
            RecordExists = false;
            return Task.CompletedTask;
        }

        public Task ReadStateAsync(CancellationToken cancellationToken) => ReadStateAsync();

        public Task WriteStateAsync(CancellationToken cancellationToken) => WriteStateAsync();

        public Task ClearStateAsync(CancellationToken cancellationToken) => ClearStateAsync();
    }

    private sealed class FakePersistentVersionedState : IPersistentState<VersionedTestState>
    {
        public FakePersistentVersionedState(VersionedTestState state)
        {
            State = state;
        }

        public string Etag { get; set; } = "etag-1";
        public bool RecordExists { get; set; } = true;
        public VersionedTestState State { get; set; }

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
