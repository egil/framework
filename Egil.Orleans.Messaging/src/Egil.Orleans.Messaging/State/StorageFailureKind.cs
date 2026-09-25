namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Classifies a storage operation failure for recovery decisions.
/// </summary>
public enum StorageFailureKind
{
    /// <summary>
    /// Unknown outcome. The operation may have persisted. Run read-back recovery.
    /// </summary>
    UnknownOutcome,

    /// <summary>
    /// The operation was rejected by an optimistic-concurrency check
    /// (for example an ETag mismatch), which proves the operation did not persist
    /// but also proves the local ETag is stale. Recovery must re-read to refresh
    /// the local baseline and always rethrow, since a coincidental value match
    /// must not hide a concurrent write.
    /// </summary>
    Conflict,

    /// <summary>
    /// The operation definitely did not persist and the stored version was not
    /// contradicted (for example authentication failure, missing container/table,
    /// or payload too large). Skip read-back and rethrow.
    /// </summary>
    DidNotPersist
}
