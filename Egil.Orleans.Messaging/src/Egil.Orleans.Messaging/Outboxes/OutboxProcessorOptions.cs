using System.Collections.Immutable;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Configuration for <see cref="OutboxProcessor{TOutbox}"/>. Defines how the
/// processor reads pending items, acknowledges successes and failures, and
/// schedules retry work.
/// </summary>
/// <typeparam name="TOutbox">
/// The base payload type of outbox messages. Must match the type parameter of the
/// <see cref="OutboxProcessor{TOutbox}"/> this options instance configures.
/// </typeparam>
public sealed class OutboxProcessorOptions<TOutbox>
    where TOutbox : notnull
{
    /// <summary>
    /// Returns the current non-null immutable outbox snapshot. Evaluated before dispatch
    /// and again during acknowledgement and retry scheduling.
    /// </summary>
    public required Func<Outbox<TOutbox>> OutboxAccessor { get; init; }

    /// <summary>
    /// Called with items that were successfully dispatched by their postmen.
    /// The grain is expected to remove these items from its outbox.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Persisting the removal immediately is the straightforward choice, but not the
    /// only one: because an item only leaves the <em>durable</em> outbox once the
    /// removal is written, deferring that write risks a redelivery and never a lost
    /// message. A grain using <c>IStateManager&lt;T&gt;</c> can therefore
    /// assign <c>State</c> and let the next business write carry it. If it does,
    /// note that this processor reconciles its retry timer and reminder against the
    /// <see cref="OutboxAccessor"/> snapshot, which reflects the deferred removal. Retry
    /// is therefore disabled on the strength of a removal that is not durable yet, and
    /// anything that later discards the change brings those items back as pending without
    /// re-arming it: a write that fails, a successful <c>ReadAsync</c> or
    /// <c>ClearAsync</c>, which let storage win, or — in a <c>[Reentrant]</c> grain or
    /// with interleaved acknowledgement — a business write that was already in flight when
    /// the assignment happened and finishes by adopting its own value. Post again in any of
    /// those cases.
    /// </para>
    /// The batch contains exactly the items that posted successfully and is
    /// <em>not necessarily a contiguous prefix</em> of the
    /// <see cref="OutboxAccessor"/> snapshot: different postmen dispatch their
    /// groups concurrently, and an item without a matching postman fails in
    /// place while later items can still succeed. Remove the received items
    /// by their <see cref="OutboxMessageEnvelope{T}.Id"/>,
    /// never by position or count — positional removal can drop a failed,
    /// undelivered item and lose it.
    /// </remarks>
    public required Func<ImmutableArray<OutboxMessageEnvelope<TOutbox>>, CancellationToken, ValueTask> AcknowledgePostedAsync { get; init; }

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
    /// the item is no longer pending after acknowledgement. Policies that must
    /// survive activation restarts (max attempts before dead-letter, etc.)
    /// should persist their own counters on the items or grain state.
    /// </remarks>
    public Func<ImmutableArray<(OutboxMessageEnvelope<TOutbox> Item, Exception Error, int Attempt)>, CancellationToken, ValueTask>? AcknowledgeFailuresAsync { get; init; }

    /// <summary>
    /// Maximum time per post run. Default: 20 seconds.
    /// </summary>
    public TimeSpan ProcessingTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Clock used to enforce <see cref="ProcessingTimeout"/>. Default:
    /// <see cref="TimeProvider.System"/>.
    /// </summary>
    /// <remarks>
    /// This provider controls only the processing timeout. Orleans owns the
    /// grain timers and reminders used for <see cref="RetryDelay"/>.
    /// </remarks>
    public TimeProvider TimeProvider { get; init; } = global::System.TimeProvider.System;

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
    /// <see cref="AcknowledgeFailuresAsync"/> use
    /// <see cref="InterleaveAcknowledgementCallbacks"/>. Snapshot reads from
    /// <see cref="OutboxAccessor"/> can also happen after a background delivery
    /// pass to decide whether retry work remains.
    /// </remarks>
    public bool Interleave { get; init; } = true;

    /// <summary>
    /// Whether the acknowledgement callbacks may interleave with other grain
    /// calls when posting runs in the background.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/> because these callbacks usually
    /// update durable outbox state. Orleans reentrancy rules still apply:
    /// reentrant grains may interleave these callbacks regardless.
    /// </remarks>
    public bool InterleaveAcknowledgementCallbacks { get; init; }

    /// <summary>
    /// Whether background retry work should keep the grain activation alive
    /// while pending outbox items remain.
    /// </summary>
    public bool KeepAlive { get; init; }
}
