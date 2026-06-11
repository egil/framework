using System.Collections.Immutable;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Configuration for <see cref="OutboxProcessor{TOutbox}"/>. Defines how the
/// processor reads pending items, reconciles successes/failures, and schedules
/// retry work.
/// </summary>
/// <typeparam name="TOutbox">
/// The base type of outbox items. Must match the type parameter of the
/// <see cref="OutboxProcessor{TOutbox}"/> this options instance configures.
/// </typeparam>
public sealed class OutboxProcessorOptions<TOutbox>
    where TOutbox : notnull
{
    /// <summary>
    /// Snapshot of pending items. Called once before each post run and again
    /// after reconciliation to decide whether retry work remains.
    /// </summary>
    public required Func<ImmutableArray<TOutbox>> PendingItems { get; init; }

    /// <summary>
    /// Called with items that were successfully dispatched by their postmen.
    /// The grain is expected to remove these items from durable outbox state
    /// and persist the update.
    /// </summary>
    /// <remarks>
    /// The batch contains exactly the items that posted successfully and is
    /// <em>not necessarily a contiguous prefix</em> of the
    /// <see cref="PendingItems"/> snapshot: different postmen dispatch their
    /// groups concurrently, and an item without a matching postman fails in
    /// place while later items can still succeed. Remove the received items
    /// themselves (for example by their <c>OutboxSequenceToken</c> when
    /// <typeparamref name="TOutbox"/> is <c>OutboxMessageEnvelope&lt;T&gt;</c>),
    /// never by position or count — positional removal can drop a failed,
    /// undelivered item and lose it.
    /// </remarks>
    public required Func<ImmutableArray<TOutbox>, CancellationToken, ValueTask> AcknowledgePostedAsync { get; init; }

    /// <summary>
    /// Called with items that failed dispatch, along with the exception and
    /// the in-memory attempt count. The grain decides whether to leave them
    /// pending, remove them, or move them to dead-letter state.
    /// </summary>
    /// <remarks>
    /// Attempt counts are tracked in memory only, keyed by item equality:
    /// they reset to one when the grain activation recycles, and item types
    /// without stable value equality (for example mutable classes mutated
    /// after enqueue) make counts restart silently. Counters are pruned when
    /// the item is no longer pending after reconciliation. Policies that must
    /// survive activation restarts (max attempts before dead-letter, etc.)
    /// should persist their own counters on the items or grain state.
    /// </remarks>
    public Func<ImmutableArray<(TOutbox Item, Exception Error, int Attempt)>, CancellationToken, ValueTask>? ReconcileFailedAsync { get; init; }

    /// <summary>
    /// Maximum time per post run. Default: 20 seconds.
    /// </summary>
    public TimeSpan ProcessingTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Delay before retrying remaining pending items. Cross-activation retry
    /// is clamped to at least one minute because Orleans reminders do not
    /// support sub-minute precision.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Whether background posting may allow other grain calls to run while
    /// postmen are awaiting asynchronous work.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="true"/> so slow delivery does not block
    /// unrelated calls to the grain. This controls the delivery phase only;
    /// <see cref="AcknowledgePostedAsync"/> and
    /// <see cref="ReconcileFailedAsync"/> use
    /// <see cref="InterleaveReconciliationCallbacks"/>. Snapshot reads from
    /// <see cref="PendingItems"/> can also happen after a background delivery
    /// pass to decide whether retry work remains.
    /// </remarks>
    public bool Interleave { get; init; } = true;

    /// <summary>
    /// Whether acknowledgement and failure reconciliation callbacks may
    /// interleave with other grain calls when posting runs in the background.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/> because these callbacks usually
    /// update durable outbox state. Orleans reentrancy rules still apply:
    /// reentrant grains may interleave these callbacks regardless.
    /// </remarks>
    public bool InterleaveReconciliationCallbacks { get; init; }

    /// <summary>
    /// Whether background retry work should keep the grain activation alive
    /// while pending outbox items remain.
    /// </summary>
    public bool KeepAlive { get; init; }
}
