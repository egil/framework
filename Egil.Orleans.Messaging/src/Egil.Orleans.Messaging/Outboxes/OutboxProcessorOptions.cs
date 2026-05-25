using System.Collections.Immutable;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Controls where outbox postman callbacks execute.
/// </summary>
public enum OutboxPostmanExecutionMode
{
    /// <summary>
    /// Executes postman callbacks on the Orleans activation scheduler.
    /// </summary>
    GrainScheduler,

    /// <summary>
    /// Executes postman callbacks on the .NET thread pool.
    /// </summary>
    ThreadPool
}

/// <summary>
/// Configuration for <see cref="OutboxProcessor{TOutbox}"/>. Defines how the
/// processor reads pending items, reconciles successes/failures, and controls
/// timer/reminder scheduling.
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
    /// Timer/reminder retry period. Reminder period is clamped to at least one
    /// minute because Orleans reminders do not support sub-minute precision.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Whether timer callbacks may interleave with other grain turns.
    /// </summary>
    public bool Interleave { get; init; }

    /// <summary>
    /// Whether an active timer keeps the grain activation alive.
    /// </summary>
    public bool KeepAlive { get; init; }

    /// <summary>
    /// Controls where postman callbacks execute.
    /// </summary>
    public OutboxPostmanExecutionMode PostmanExecution { get; init; } =
        OutboxPostmanExecutionMode.GrainScheduler;
}