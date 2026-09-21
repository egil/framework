using System.Collections;
using System.Collections.Immutable;
using Egil.Orleans.Messaging.Outboxes;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Egil.Orleans.Messaging.Journaling;

internal sealed class DurableOutbox<T> : IDurableOutbox<T>, IJournaledState, IDurableValueCommandHandler<OutboxOperation<T>>
{
    private readonly TimeProvider time;
    private readonly IDurableValueCommandCodec<OutboxOperation<T>> codec;
    private Outbox<T> original;
    private Outbox<T> current;
    private Outbox<T>? snapshotBeingWritten;
    private bool bound;

    public DurableOutbox(
        [ServiceKey] string name,
        IJournaledStateManager manager,
        TimeProvider time,
        JournalCodec<OutboxOperation<T>> codec) : this(time, codec.Value)
    {
        manager.RegisterState(name, this);
    }

    private DurableOutbox(TimeProvider time, IDurableValueCommandCodec<OutboxOperation<T>> codec)
    {
        this.time = time;
        this.codec = codec;
        original = current = Outbox<T>.Create();
    }

    public Outbox<T> AsImmutable() => current;
    public Guid Revision => current.Revision;
    public long LatestSequenceNumber => current.LatestSequenceNumber;
    public DateTimeOffset? Epoch => current.Epoch;
    public int Count => current.Count;
    public bool IsEmpty => current.IsEmpty;
    public T this[int index] => current[index];
    public ImmutableArray<OutboxMessageEnvelope<T>> Envelopes => current.Envelopes;
    public IEnumerator<T> GetEnumerator() => current.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IDurableOutbox<T> Add(T message) => Add(message, time.GetUtcNow());
    public IDurableOutbox<T> Add(T message, DateTimeOffset utcNow) => Update(current.Add(message, utcNow));
    public IDurableOutbox<T> AddRange(IEnumerable<T> messages) => AddRange(messages, time.GetUtcNow());
    public IDurableOutbox<T> AddRange(IEnumerable<T> messages, DateTimeOffset utcNow) => Update(current.AddRange(messages, utcNow));
    public IDurableOutbox<T> Remove(OutboxMessageEnvelope<T> item) => Update(current.Remove(item));
    public IDurableOutbox<T> Remove(OutboxMessageId id) => Update(current.Remove(id));
    public IDurableOutbox<T> RemoveRange(IEnumerable<OutboxMessageEnvelope<T>> items) => Update(current.RemoveRange(items));
    public IDurableOutbox<T> RemoveRange(IEnumerable<OutboxMessageId> ids) => Update(current.RemoveRange(ids));
    public IDurableOutbox<T> Clear() => Update(current.Clear());

    private IDurableOutbox<T> Update(Outbox<T> next)
    {
        if (!bound)
        {
            throw new InvalidOperationException("The component must be bound by the journal manager before it can be changed.");
        }

        // Immutable mutations finish before replacing the reference, so failed batch
        // enumeration leaves the outbox intact. Encoding is deferred until Orleans gathers a write.
        current = next;
        return this;
    }

    void IJournaledState.Reset(JournalStreamWriter writer)
    {
        original = current = Outbox<T>.Create();
        snapshotBeingWritten = null;
        bound = true;
    }

    void IJournaledState.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, codec).Apply(entry.Reader, this);

    void IDurableValueCommandHandler<OutboxOperation<T>>.ApplySet(OutboxOperation<T> operation)
    {
        if (operation.Kind == "snapshot")
        {
            current = operation.State ?? throw new InvalidOperationException("Missing outbox snapshot.");
            return;
        }

        if (operation.Kind != "delta")
        {
            throw new InvalidOperationException($"Unknown outbox operation '{operation.Kind}'.");
        }

        var envelopes = current.Envelopes;
        if (operation.Removed is { } removed)
        {
            var ids = removed.ToHashSet();
            envelopes = envelopes.Where(item => !ids.Contains(item.Id)).ToImmutableArray();
        }

        if (operation.Added is { } added)
        {
            envelopes = envelopes.AddRange(added);
        }

        // Apply recorded identities and metadata directly. Calling Add or Restore during
        // replay would capture fresh timestamps, revisions, or an incorrect empty epoch.
        current = new Outbox<T>(operation.LatestSequenceNumber, envelopes, operation.Epoch, operation.Revision);
    }

    void IJournaledState.OnRecoveryCompleted() => original = current;

    void IJournaledState.AppendEntries(JournalStreamWriter writer)
    {
        snapshotBeingWritten = null;
        if (ReferenceEquals(original, current))
        {
            return;
        }

        var originalIds = original.Envelopes.Select(item => item.Id).ToHashSet();
        var currentIds = current.Envelopes.Select(item => item.Id).ToHashSet();
        var added = current.Envelopes.Where(item => !originalIds.Contains(item.Id)).ToArray();
        var removed = original.Envelopes.Where(item => !currentIds.Contains(item.Id)).Select(item => item.Id).ToArray();
        codec.WriteSet(Operation("delta", current) with
        {
            Added = added.Length == 0 ? null : added,
            Removed = removed.Length == 0 ? null : removed
        }, writer);

        // The manager retains encoded append entries after a storage failure. Advance the
        // diff baseline only after encoding succeeds, so a retry adds just the later changes.
        // This reference is a journal baseline, not a separate user-visible committed view.
        original = current;
    }

    void IJournaledState.AppendSnapshot(JournalStreamWriter writer)
    {
        snapshotBeingWritten = null;
        codec.WriteSet(Operation("snapshot", current) with { State = current }, writer);
        // Compaction uses a temporary writer which is discarded on failure. Its baseline
        // must wait for acknowledgement, unlike append entries retained in the shared writer.
        snapshotBeingWritten = current;
    }

    void IJournaledState.OnWriteCompleted()
    {
        if (snapshotBeingWritten is { } snapshot)
        {
            // A later mutation may have changed current while storage awaited acknowledgement.
            original = snapshot;
            snapshotBeingWritten = null;
        }
    }

    IJournaledState IJournaledState.DeepCopy() => new DurableOutbox<T>(time, codec)
    {
        original = original,
        current = current
    };

    private static OutboxOperation<T> Operation(string kind, Outbox<T> state) =>
        new(kind, state.LatestSequenceNumber, state.Epoch, state.Revision);
}

internal sealed record OutboxOperation<T>(string Kind, long LatestSequenceNumber, DateTimeOffset? Epoch, Guid Revision)
{
    public OutboxMessageEnvelope<T>[]? Added { get; init; }
    public OutboxMessageId[]? Removed { get; init; }
    public Outbox<T>? State { get; init; }
}
