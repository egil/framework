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
    /// The operation definitely did not persist. Skip read-back and rethrow.
    /// </summary>
    DidNotPersist
}