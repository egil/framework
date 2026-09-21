#pragma warning disable ORLEANSEXP005 // A storage-boundary recorder around the real Orleans journal implementation.
using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using Orleans.Journaling;
using Orleans.Journaling.Json;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class RecordingJournalStorageProvider : IJournalStorageProvider
{
    private readonly ConcurrentDictionary<JournalId, RecordingJournalStorage> journals = new();
    public IJournalStorage CreateStorage(JournalId journalId) => journals.GetOrAdd(journalId, static _ => new());
    public RecordingJournalStorage For(JournalId journalId) => journals[journalId];
    public RecordingJournalStorage For(GrainId grainId) => journals[JournalId.FromGrainId(grainId)];
}

public sealed class RecordingJournalStorage : IJournalStorage
{
    private readonly VolatileJournalStorage storage = new(JsonJournalExtensions.JournalFormatKey);
    private readonly List<string> writes = [];
    public IReadOnlyList<string> Writes { get { lock (writes) { return writes.ToArray(); } } }
    private PausedJournalWrite? pause;
    public bool CompactNext { get; set; }
    public JournalFailure NextFailure { get; set; }
    public PausedJournalWrite PauseNextWrite()
    {
        var result = new PausedJournalWrite();
        if (Interlocked.CompareExchange(ref pause, result, null) is not null)
        {
            throw new InvalidOperationException("A write is already paused.");
        }

        return result;
    }

    private async Task<JournalFailure> BeforeWriteAsync(CancellationToken cancellationToken)
    {
        var gate = Interlocked.Exchange(ref pause, null);
        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken);
        }

        var failure = NextFailure;
        NextFailure = JournalFailure.None;
        if (failure == JournalFailure.BeforeCommit)
        {
            throw new IOException("Injected failure before journal commit.");
        }

        return failure;
    }

    private static void AfterWrite(JournalFailure failure)
    {
        if (failure == JournalFailure.AfterCommit)
        {
            throw new IOException("Injected lost storage acknowledgement.");
        }
    }
    public bool IsCompactionRequested => CompactNext;

    public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken) =>
        storage.ReadAsync(consumer, cancellationToken);

    public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        var failure = await BeforeWriteAsync(cancellationToken);
        await storage.AppendAsync(value, cancellationToken);
        lock (writes) { writes.Add(Encoding.UTF8.GetString(value.ToArray())); }
        AfterWrite(failure);
    }

    public async ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        var failure = await BeforeWriteAsync(cancellationToken);
        await storage.ReplaceAsync(value, cancellationToken);
        CompactNext = false;
        lock (writes) { writes.Add(Encoding.UTF8.GetString(value.ToArray())); }
        AfterWrite(failure);
    }

    public ValueTask DeleteAsync(CancellationToken cancellationToken) => storage.DeleteAsync(cancellationToken);
}

public enum JournalFailure { None, BeforeCommit, AfterCommit }

public sealed class PausedJournalWrite : IAsyncDisposable
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task observed = Task.CompletedTask;

    public Task Started => started.Task;
    public void Observe(Task task) => observed = task;
    public void Release() => release.TrySetResult();

    internal async Task WaitAsync(CancellationToken cancellationToken)
    {
        started.TrySetResult();
        await release.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        // Assertion failures and timeouts must release storage and observe the pending grain call.
        Release();
        await observed.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
    }
}
