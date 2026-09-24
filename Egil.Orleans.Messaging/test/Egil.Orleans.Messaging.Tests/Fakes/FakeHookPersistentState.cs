namespace Egil.Orleans.Messaging.Tests.Fakes;

internal sealed record HookSnapshot(string Value);

internal sealed class FakeHookPersistentState(HookSnapshot? persisted) : IPersistentState<HookSnapshot>
{
    public HookSnapshot? Persisted { get; private set; } = persisted;
    public HookSnapshot State { get; set; } = persisted!;
    public string Etag => "etag";
    public bool RecordExists => Persisted is not null;
    public Exception? MutationError { get; init; }
    public Exception? ReadError { get; init; }
    public bool PersistBeforeError { get; init; }
    public CancellationTokenSource? CancelAfterCommit { get; init; }
    public TaskCompletionSource? Started { get; init; }
    public Task? Release { get; init; }
    public Func<Task>? BeforeRead { get; init; }
    public int Reads { get; private set; }
    public int Mutations { get; private set; }
    public async Task ReadStateAsync()
    {
        Reads++;
        if (BeforeRead is not null) await BeforeRead();
        if (ReadError is not null) throw ReadError;
        State = Persisted!;
    }
    public Task WriteStateAsync() => Mutate(false);
    public Task ClearStateAsync() => Mutate(true);
    public Task ReadStateAsync(CancellationToken token) => ReadStateAsync();
    public Task WriteStateAsync(CancellationToken token) => WriteStateAsync();
    public Task ClearStateAsync(CancellationToken token) => ClearStateAsync();
    private async Task Mutate(bool clear)
    {
        Mutations++;
        Started?.TrySetResult();
        if (Release is not null) await Release;
        if (MutationError is null || PersistBeforeError) Persisted = clear ? null : State;
        if (CancelAfterCommit is not null) await CancelAfterCommit.CancelAsync();
        if (MutationError is not null) throw MutationError;
    }
}

