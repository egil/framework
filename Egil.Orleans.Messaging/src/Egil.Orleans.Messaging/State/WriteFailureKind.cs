namespace Egil.Orleans.Messaging.State;

/// <summary>
/// Classifies a storage write failure for recovery decisions.
/// </summary>
public enum WriteFailureKind
{
    /// <summary>
    /// Unknown outcome. The write may have persisted. Run read-back recovery.
    /// </summary>
    UnknownOutcome,

    /// <summary>
    /// The write definitely did not persist. Skip read-back and rethrow.
    /// </summary>
    DidNotPersist
}
