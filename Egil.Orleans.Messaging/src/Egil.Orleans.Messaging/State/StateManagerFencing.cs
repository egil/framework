namespace Egil.Orleans.Messaging.State;

// Local snapshots remain readable after fencing. Keep the activation's failure
// evidence separate so shutdown can distinguish conflicts from uncertain work.
internal sealed class StateManagerFencing(StorageFailureKind failureKind)
{
    public bool OnlyConflicts { get; private set; } = failureKind is StorageFailureKind.Conflict;

    // Several managers can fail before an activation finishes its current call.
    // A conflict on one must not hide an uncertain outcome on another.
    public void Record(StorageFailureKind failureKind)
        => OnlyConflicts &= failureKind is StorageFailureKind.Conflict;
}
