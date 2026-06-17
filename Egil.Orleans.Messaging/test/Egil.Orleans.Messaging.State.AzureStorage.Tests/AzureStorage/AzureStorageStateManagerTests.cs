using Azure;
using Azure.Data.Tables.Models;
using Azure.Storage.Blobs.Models;

namespace Egil.Orleans.Messaging.State.AzureStorage.Tests.AzureStorage;

public sealed class AzureStorageStateManagerTests
{
    [Fact]
    public async Task WriteAsync_with_precondition_failed_skips_read_back_and_rethrows()
    {
        var exception = new RequestFailedException(412, "Precondition failed.");
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = exception
        };
        var manager = new AzureStorageStateManager<TestState>(storage);

        var actual = await Assert.ThrowsAsync<RequestFailedException>(
            () => manager.WriteAsync(new TestState("next")));

        Assert.Same(exception, actual);
        Assert.Equal(0, storage.ReadCount);
        Assert.Equal(new TestState("initial"), manager.State);
        Assert.Equal(new TestState("initial"), storage.State);
    }

    [Fact]
    public async Task WriteAsync_with_nested_precondition_failed_skips_read_back_and_rethrows_outer_exception()
    {
        var exception = new InvalidOperationException(
            "wrapped",
            new RequestFailedException(412, "Precondition failed."));
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = exception
        };
        var manager = new AzureStorageStateManager<TestState>(storage);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.WriteAsync(new TestState("next")));

        Assert.Same(exception, actual);
        Assert.Equal(0, storage.ReadCount);
        Assert.Equal(new TestState("initial"), manager.State);
    }

    [Fact]
    public async Task WriteAsync_with_conflict_skips_read_back_and_rethrows()
    {
        var exception = new RequestFailedException(
            409,
            "The specified blob already exists.",
            BlobErrorCode.BlobAlreadyExists.ToString(),
            innerException: null);
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = exception
        };
        var manager = new AzureStorageStateManager<TestState>(storage);

        var actual = await Assert.ThrowsAsync<RequestFailedException>(
            () => manager.WriteAsync(new TestState("next")));

        Assert.Same(exception, actual);
        Assert.Equal(0, storage.ReadCount);
        Assert.Equal(new TestState("initial"), manager.State);
    }

    [Fact]
    public async Task WriteAsync_with_auth_failure_skips_read_back_and_rethrows()
    {
        var exception = new RequestFailedException(
            403,
            "Authentication failed.",
            BlobErrorCode.AuthenticationFailed.ToString(),
            innerException: null);
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = exception
        };
        var manager = new AzureStorageStateManager<TestState>(storage);

        var actual = await Assert.ThrowsAsync<RequestFailedException>(
            () => manager.WriteAsync(new TestState("next")));

        Assert.Same(exception, actual);
        Assert.Equal(0, storage.ReadCount);
        Assert.Equal(new TestState("initial"), manager.State);
    }

    [Fact]
    public async Task WriteAsync_with_transient_storage_failure_uses_read_back_recovery()
    {
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = new RequestFailedException(503, "Server busy."),
            OnRead = state => state.State = new TestState("next")
        };
        var manager = new AzureStorageStateManager<TestState>(storage);

        await manager.WriteAsync(new TestState("next"));

        Assert.Equal(1, storage.ReadCount);
        Assert.Equal(new TestState("next"), manager.State);
    }

    [Fact]
    public async Task WriteAsync_with_operation_timed_out_uses_read_back_recovery()
    {
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = new RequestFailedException(
                500,
                "The operation could not be completed within the permitted time.",
                TableErrorCode.OperationTimedOut.ToString(),
                innerException: null),
            OnRead = state => state.State = new TestState("next")
        };
        var manager = new AzureStorageStateManager<TestState>(storage);

        await manager.WriteAsync(new TestState("next"));

        Assert.Equal(1, storage.ReadCount);
        Assert.Equal(new TestState("next"), manager.State);
    }

    [Fact]
    public async Task WriteAsync_with_no_response_uses_read_back_recovery()
    {
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = new RequestFailedException(
                0,
                "No response was received.",
                errorCode: null,
                innerException: null),
            OnRead = state => state.State = new TestState("next")
        };
        var manager = new AzureStorageStateManager<TestState>(storage);

        await manager.WriteAsync(new TestState("next"));

        Assert.Equal(1, storage.ReadCount);
        Assert.Equal(new TestState("next"), manager.State);
    }

    [Fact]
    public async Task WriteAsync_with_timeout_exception_uses_read_back_recovery()
    {
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = new TimeoutException("storage timeout"),
            OnRead = state => state.State = new TestState("next")
        };
        var manager = new AzureStorageStateManager<TestState>(storage);

        await manager.WriteAsync(new TestState("next"));

        Assert.Equal(1, storage.ReadCount);
        Assert.Equal(new TestState("next"), manager.State);
    }

    [Fact]
    public async Task WriteAsync_with_aggregate_precondition_failed_skips_read_back_and_rethrows()
    {
        var exception = new AggregateException(
            new RequestFailedException(
                412,
                "The update condition specified in the request was not satisfied.",
                TableErrorCode.UpdateConditionNotSatisfied.ToString(),
                innerException: null));
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = exception
        };
        var manager = new AzureStorageStateManager<TestState>(storage);

        var actual = await Assert.ThrowsAsync<AggregateException>(
            () => manager.WriteAsync(new TestState("next")));

        Assert.Same(exception, actual);
        Assert.Equal(0, storage.ReadCount);
        Assert.Equal(new TestState("initial"), manager.State);
    }

    [Fact]
    public async Task WriteAsync_with_mixed_aggregate_failure_uses_read_back_recovery()
    {
        var storage = new FakePersistentState(new TestState("initial"))
        {
            WriteException = new AggregateException(
                new RequestFailedException(
                    412,
                    "Precondition failed.",
                    BlobErrorCode.ConditionNotMet.ToString(),
                    innerException: null),
                new RequestFailedException(
                    503,
                    "Server busy.",
                    BlobErrorCode.ServerBusy.ToString(),
                    innerException: null)),
            OnRead = state => state.State = new TestState("next")
        };
        var manager = new AzureStorageStateManager<TestState>(storage);

        await manager.WriteAsync(new TestState("next"));

        Assert.Equal(1, storage.ReadCount);
        Assert.Equal(new TestState("next"), manager.State);
    }

    [Fact]
    public async Task ClearAsync_with_precondition_failed_skips_read_back_and_rethrows()
    {
        var exception = new RequestFailedException(412, "Precondition failed.");
        var storage = new FakePersistentState(new TestState("initial"))
        {
            ClearException = exception
        };
        var manager = new AzureStorageStateManager<TestState>(storage);

        var actual = await Assert.ThrowsAsync<RequestFailedException>(
            () => manager.ClearAsync());

        Assert.Same(exception, actual);
        Assert.Equal(0, storage.ReadCount);
        Assert.Equal(new TestState("initial"), manager.State);
        Assert.Equal(new TestState("initial"), storage.State);
    }

    [Fact]
    public async Task ClearAsync_with_transient_storage_failure_uses_read_back_recovery()
    {
        var storage = new FakePersistentState(new TestState("initial"))
        {
            ClearException = new RequestFailedException(503, "Server busy."),
            OnRead = state =>
            {
                state.State = null!;
                state.RecordExists = false;
            }
        };
        var manager = new AzureStorageStateManager<TestState>(storage);

        await manager.ClearAsync();

        Assert.Equal(1, storage.ReadCount);
        Assert.False(storage.RecordExists);
        Assert.Null(manager.State);
    }

    private sealed record TestState(string Value);

    private sealed class FakePersistentState(TestState state) : IPersistentState<TestState>
    {
        public Exception? WriteException { get; set; }

        public Exception? ClearException { get; set; }

        public Action<FakePersistentState>? OnRead { get; set; }

        public int ReadCount { get; private set; }

        public string Etag { get; set; } = "etag-1";

        public bool RecordExists { get; set; } = true;

        public TestState State { get; set; } = state;

        public Task ReadStateAsync()
        {
            ReadCount++;
            OnRead?.Invoke(this);
            return Task.CompletedTask;
        }

        public Task WriteStateAsync()
        {
            if (WriteException is not null)
            {
                throw WriteException;
            }

            return Task.CompletedTask;
        }

        public Task ClearStateAsync()
        {
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
}