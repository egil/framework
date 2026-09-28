namespace Egil.Orleans.Messaging.State;

/// <summary>How a manager handles a failed storage write or clear.</summary>
/// <remarks>
/// Fencing is the safer default because replacing the activation also discards private
/// grain fields which storage reconciliation cannot repair. Both approaches normally
/// read storage before work resumes; fencing adds grain construction and activation work.
/// Read-back can be preferable when rebuilding private state is expensive and the grain
/// can safely retain that state after reconciliation. Neither policy retries the command.
/// </remarks>
public enum StateRecoveryPolicy
{
    /// <summary>
    /// Permanently reject mutations and storage reads, request grain deactivation when
    /// a context is available, and rethrow the storage failure without attempting read-back.
    /// The local state snapshot and unsaved-changes marker remain readable.
    /// Even a write that committed before losing its response is reported as failed.
    /// Subsequent mutations, storage reads and hook configuration throw <see cref="StateManagerFencedException"/> with the
    /// storage failure's classification and original exception.
    /// This is the default policy.
    /// Recovery comparison, including <see cref="VersionedState.Version"/>, is skipped;
    /// ordinary immutable records are sufficient. A subsequent activation loads storage.
    /// </summary>
    FenceAndDeactivate = 0,

    /// <summary>
    /// Classify the failure and, when its outcome is uncertain, read storage back.
    /// A confirmed lost response can be reported as success. Select this explicitly
    /// to keep an activation usable after a failed storage mutation.
    /// <see cref="VersionedState"/> recognizes persisted writes without depending on
    /// structural equality. Private grain state remains the application's responsibility.
    /// </summary>
    ReadBack = 1,
}
