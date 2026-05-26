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
    public required Func<ImmutableArray<TOutbox>, CancellationToken, ValueTask>
        AcknowledgePostedAsync
    { get; init; }

    /// <summary>
    /// Called with items that failed dispatch, along with the exception and
    /// the in-memory attempt count. The grain decides whether to leave them
    /// pending, remove them, or move them to dead-letter state.
    /// </summary>
    public Func<ImmutableArray<(TOutbox Item, Exception Error, int Attempt)>,
        CancellationToken, ValueTask>? ReconcileFailedAsync
    { get; init; }

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
    /// acknowledgement and failure callbacks use
    /// <see cref="InterleaveReconciliationCallbacks"/>.
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
