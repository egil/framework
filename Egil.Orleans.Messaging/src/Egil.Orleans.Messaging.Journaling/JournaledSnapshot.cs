using Orleans.Journaling;

namespace Egil.Orleans.Messaging.Journaling;

internal abstract class JournaledSnapshot<TSnapshot, TOperation>(
    TSnapshot initial,
    IDurableValueCommandCodec<TOperation> codec) : IStateMachine, IDurableValueCommandHandler<TOperation>
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

    void IStateMachine.Reset(JournalStreamWriter journalWriter)
    {
        writer = journalWriter;
        Current = Empty();
        // Recovery retries reset these same instances. The owner waits for initialization
        // before staging changes so replay never races with application mutations.
        bound = true;
    }

    void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
        context.GetRequiredCommandCodec(entry.FormatKey, codec).Apply(entry.Reader, this);

    void IDurableValueCommandHandler<TOperation>.ApplySet(TOperation operation) =>
        Current = Apply(Current, operation);

    // Mutations already encode entries and update the sole view. Journal acknowledgements
    // do not publish a second view; the owning grain decides when to save and when to post.
    void IStateMachine.OnRecoveryCompleted() { }
    void IStateMachine.WritePendingEntries(JournalStreamWriter journalWriter) { }
    void IStateMachine.OnWriteCompleted() { }

    void IStateMachine.WriteSnapshot(JournalStreamWriter journalWriter) =>
        codec.WriteSet(Snapshot(Current), journalWriter);
}
