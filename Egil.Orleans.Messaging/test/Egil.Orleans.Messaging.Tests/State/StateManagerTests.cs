namespace Egil.Orleans.Messaging.Tests.State;

public sealed class StateManagerTests
{
    [Fact]
    public async Task ReadAsync_refreshes_state_from_storage()
    {
        var storage = new FakePersistentState(new TestState("initial"));
        var manager = new DefaultStateManager<TestState>(storage);
        storage.State = new TestState("fresh");

        await manager.ReadAsync();

        Assert.Equal(new TestState("fresh"), manager.State);
    }

    [Fact]
    public async Task WriteAsync_success_updates_committed_state()
    {
        var storage = new FakePersistentState(new TestState("initial"));
        var manager = new DefaultStateManager<TestState>(storage);
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
        var manager = new DefaultStateManager<TestState>(storage);
        var next = new TestState("next");

        await manager.WriteAsync(next);

        Assert.Equal(next, manager.State);
    }

    [Fact]
    public async Task WriteAsync_when_write_fails_and_read_shows_different_state_rethrows_original_exception()
    {
        var writeException = new TimeoutException("write timeout");
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = writeException,
            OnRead = state => state.State = new TestState("other")
        };
        var manager = new DefaultStateManager<TestState>(storage);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => manager.WriteAsync(new TestState("next")));

        Assert.Same(writeException, ex);
        Assert.Equal(new TestState("initial"), manager.State);
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
        var manager = new DefaultStateManager<TestState>(storage);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => manager.WriteAsync(new TestState("next")));

        Assert.Same(writeException, ex);
        Assert.Equal(new TestState("initial"), manager.State);
    }

    [Fact]
    public async Task WriteAsync_on_versioned_state_stamps_new_version()
    {
        var original = new VersionedTestState("initial");
        var storage = new FakePersistentVersionedState(original);
        var manager = new DefaultStateManager<VersionedTestState>(storage);
        var next = new VersionedTestState("next") { Version = Guid.Empty };

        await manager.WriteAsync(next);

        Assert.NotEqual(Guid.Empty, next.Version);
        Assert.Equal(next.Version, manager.State.Version);
    }

    [Fact]
    public async Task ClearAsync_success_updates_committed_state()
    {
        var storage = new FakePersistentState(new TestState("initial"));
        var manager = new DefaultStateManager<TestState>(storage);

        await manager.ClearAsync();

        Assert.Null(manager.State);
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
        var manager = new DefaultStateManager<TestState>(storage);

        await manager.ClearAsync();

        Assert.Null(manager.State);
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
        var manager = new DefaultStateManager<TestState>(storage);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => manager.ClearAsync());

        Assert.Same(clearException, ex);
        Assert.Equal(new TestState("initial"), manager.State);
    }

    private sealed record TestState(string Value);

    private sealed record VersionedTestState(string Value) : VersionedState, IEquatable<VersionedTestState>;

    private sealed class FakePersistentState : IPersistentState<TestState>
    {
        public FakePersistentState(TestState state)
        {
            State = state;
        }

        public Exception? ReadException { get; set; }
        public Exception? WriteException { get; set; }
        public Exception? ClearException { get; set; }
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
            OnWrite?.Invoke(this);
            if (WriteException is not null)
            {
                throw WriteException;
            }

            return Task.CompletedTask;
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
