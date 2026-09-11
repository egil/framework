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

        await manager.ReadAsync();

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

        await manager.ReadAsync();

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

        await manager.WriteAsync(next);

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

        await manager.WriteAsync(next);

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

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => manager.WriteAsync(new TestState("next")));

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
            () => manager.WriteAsync(attempted));

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
            () => manager.WriteAsync(attempted));

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

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => manager.WriteAsync(new TestState("next")));

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
            () => manager.WriteAsync(new TestState("next")));

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

        await manager.WriteAsync(next);

        Assert.NotEqual(Guid.Empty, next.Version);
        Assert.Equal(next.Version, manager.State!.Version);
    }

    [Fact]
    public async Task ClearAsync_success_updates_committed_state()
    {
        var storage = new FakePersistentState(new TestState("initial"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));

        await manager.ClearAsync();

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

        await manager.ClearAsync();
        await manager.WriteAsync(next);

        Assert.Equal(next, manager.State);
        Assert.Equal(next, storage.State);
    }

    [Fact]
    public async Task WriteAsync_from_cleared_state_when_recovery_read_is_missing_keeps_default()
    {
        var writeException = new TimeoutException("write timeout");
        var storage = new FakePersistentState(new TestState("initial"));
        var manager = new DefaultStateManager<TestState>(storage, static () => new("default"));
        await manager.ClearAsync();
        storage.WriteException = writeException;
        storage.OnRead = state =>
        {
            state.State = null!;
            state.RecordExists = false;
        };

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => manager.WriteAsync(new TestState("next")));

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

        await manager.ClearAsync();

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

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => manager.ClearAsync());

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

        var ex = await Assert.ThrowsAsync<InconsistentStateException>(() => manager.ClearAsync());

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

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => manager.ClearAsync());

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

        var error = await Assert.ThrowsAsync<TimeoutException>(() => manager.WriteAsync(new("default")));

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

        await manager.ReadAsync();
        await manager.ClearAsync();

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

        var write = manager.WriteAsync(next);

        Assert.Same(previous, manager.State);
        Assert.Same(next, storage.State);
        completion.SetResult();
        await write;
        Assert.Same(next, manager.State);
    }

    private sealed record TestState(string Value);

    private sealed record VersionedTestState(string Value) : VersionedState, IEquatable<VersionedTestState>;

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

            return WriteCompletion ?? Task.CompletedTask;
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
