using Orleans.Journaling;

namespace Egil.Orleans.Messaging.Journaling;

// The pinned package calls this protocol IJournaledState; current Orleans main calls it IStateMachine.
// See https://github.com/dotnet/orleans/blob/137d9acc17830f15b13a4eb0058d6cee633cad5e/src/Orleans.Journaling/IJournaledState.cs
internal abstract class JournaledSnapshot<TSnapshot, TOperation>(
    TSnapshot initial,
    IDurableValueCommandCodec<TOperation> codec) : IJournaledState, IDurableValueCommandHandler<TOperation>
    where TSnapshot : class
{
    private JournalStreamWriter writer;
    private bool bound;

    protected TSnapshot Current { get; private set; } = initial;

    public TSnapshot AsImmutable() => Current;

    protected void Stage(TSnapshot next, TOperation operation)
    {
        if (!bound)
        {
            throw new InvalidOperationException("The component must be bound by the journal manager before it can be changed.");
        }

        // Encode before publishing the working snapshot. A serializer exception cannot expose
        // a mutation for which no complete journal entry exists.
        codec.WriteSet(operation, writer);
        Current = next;
    }

    protected abstract TSnapshot Empty();
    protected abstract TOperation Snapshot(TSnapshot value);
    protected abstract TSnapshot Apply(TSnapshot value, TOperation operation);
    protected abstract JournaledSnapshot<TSnapshot, TOperation> CreateCopy();

    void IJournaledState.Reset(JournalStreamWriter journalWriter)
    {
        writer = journalWriter;
        Current = Empty();
        // 10.3.1 binds brand-new streams after its recovery callback. Reset establishes
        // the writer; the owning grain/session still waits for manager initialization.
        bound = true;
    }

    void IJournaledState.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, codec).Apply(entry.Reader, this);

    void IDurableValueCommandHandler<TOperation>.ApplySet(TOperation operation) =>
        Current = Apply(Current, operation);

    // Mutations already encode entries and update the sole view. Journal acknowledgements
    // do not publish a second view; the owning grain decides when to save and when to post.
    void IJournaledState.OnRecoveryCompleted() { }
    void IJournaledState.AppendEntries(JournalStreamWriter journalWriter) { }
    void IJournaledState.OnWriteCompleted() { }

    void IJournaledState.AppendSnapshot(JournalStreamWriter journalWriter) =>
        codec.WriteSet(Snapshot(Current), journalWriter);

    IJournaledState IJournaledState.DeepCopy()
    {
        // Immutable payloads allow snapshots to be shared. A copy has no live journal writer;
        // Orleans must bind/reset it before use. This member was removed on current main.
        var copy = CreateCopy();
        copy.Current = Current;
        return copy;
    }
}
