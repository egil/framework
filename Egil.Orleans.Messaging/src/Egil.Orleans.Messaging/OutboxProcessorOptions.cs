using System.Collections.Immutable;

namespace Egil.Orleans.Messaging;

/// <summary>
/// Configuration for <see cref="OutboxProcessor{TOutbox}"/>. Defines how the
/// processor reads pending items, reports successes/failures, and controls
/// timing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Callback model:</b> The processor uses callbacks rather than DI services
/// so the grain controls dispatch logic and can pass its own state to the
/// handlers. This keeps the processor grain-scoped with no ambient dependencies.
/// </para>
/// <para>
/// <b>Required callbacks:</b>
/// <list type="bullet">
/// <item><see cref="GetPending"/> — snapshot of pending items, called once
/// per post run.</item>
/// <item><see cref="OnPostCompletedAsync"/> — items successfully posted.
/// The grain must remove them from its backing collection and persist.</item>
/// </list>
/// </para>
/// <para>
/// <b>Optional callback:</b>
/// <list type="bullet">
/// <item><see cref="OnPostErrorAsync"/> — failed items with exception and
/// attempt count. If <c>null</c>, failed items retry silently on the next
/// run. The grain decides: leave the item in state to retry, or remove it
/// to dead-letter after N attempts.</item>
/// </list>
/// </para>
/// </remarks>
/// <typeparam name="TOutbox">
/// The base type of outbox items. Must match the type parameter of the
/// <see cref="OutboxProcessor{TOutbox}"/> this options instance configures.
/// </typeparam>
public sealed class OutboxProcessorOptions<TOutbox>
    where TOutbox : notnull
{
    /// <summary>
    /// Snapshot of pending items. Called once per post run. The processor
    /// iterates the returned array and dispatches each item to its matching
    /// postman.
    /// </summary>
    /// <remarks>
    /// Typically implemented as a lambda reading the grain's current outbox:
    /// <code>
    /// GetPending = () => stateManager.State.Outbox.Select(e => e.Message).ToImmutableArray()
    /// </code>
    /// </remarks>
    public required Func<ImmutableArray<TOutbox>> GetPending { get; init; }

    /// <summary>
    /// Called with the items that were successfully dispatched by their postmen.
    /// The grain must remove these items from its backing collection (e.g.,
    /// <see cref="Outbox{T}.RemoveRange"/>) and persist the updated state.
    /// </summary>
    /// <remarks>
    /// Exceptions thrown from this callback propagate out of
    /// <see cref="OutboxProcessor{TOutbox}.PostAsync"/> — the processor does
    /// not catch callback failures.
    /// </remarks>
    public required Func<ImmutableArray<TOutbox>, CancellationToken, ValueTask>
        OnPostCompletedAsync { get; init; }

    /// <summary>
    /// Called with items that failed dispatch, along with the exception and
    /// the in-memory attempt count (resets on grain reactivation). The grain
    /// decides: leave the item in state to retry on the next run, or remove
    /// it to dead-letter after N attempts.
    /// </summary>
    /// <remarks>
    /// If <c>null</c>, failed items retry silently on the next post run.
    /// Exceptions thrown from this callback propagate out of
    /// <see cref="OutboxProcessor{TOutbox}.PostAsync"/>.
    /// </remarks>
    public Func<ImmutableArray<(TOutbox Item, Exception Error, int Attempt)>,
        CancellationToken, ValueTask>? OnPostErrorAsync { get; init; }

    /// <summary>
    /// Maximum time per post run. Set below the grain's response timeout to
    /// avoid grain-level timeouts during dispatch. Default: 20 seconds.
    /// </summary>
    public TimeSpan ProcessingTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Timer and reminder period. Controls how frequently the processor
    /// retries pending items. Default: 2 minutes.
    /// </summary>
    /// <remarks>
    /// Orleans reminders fire at most once per minute, so values below 1
    /// minute are effectively timer-only (the reminder still fires at its
    /// minimum interval for cross-activation recovery).
    /// </remarks>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(2);
}
