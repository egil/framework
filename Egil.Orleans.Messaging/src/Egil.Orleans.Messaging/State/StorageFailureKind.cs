namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Classifies a storage operation failure for recovery decisions.
/// </summary>
/// <remarks>
/// Both recovery policies use this evidence. FenceAndDeactivate preserves it in
/// <see cref="StateManagerFencedException"/>; ReadBack uses it to choose reconciliation.
/// </remarks>
public enum StorageFailureKind
{
    /// <summary>
    /// The operation may have persisted. ReadBack attempts recovery by reading storage.
    /// </summary>
    UnknownOutcome,

    /// <summary>
    /// The operation was rejected by an optimistic-concurrency check
    /// (for example an ETag mismatch), which proves the operation did not persist
    /// but also proves the local ETag is stale. ReadBack must re-read to refresh
    /// the local baseline and always rethrow, since a coincidental value match
    /// must not hide a concurrent write. This does not prove another activation is alive.
    /// </summary>
    Conflict,

    /// <summary>
    /// The operation definitely did not persist and the stored version was not
    /// contradicted (for example authentication failure, missing container/table,
    /// or payload too large). ReadBack skips the recovery read and rethrows.
    /// </summary>
    DidNotPersist
}
