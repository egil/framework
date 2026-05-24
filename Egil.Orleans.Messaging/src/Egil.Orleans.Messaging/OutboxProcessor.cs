namespace Egil.Orleans.Messaging;

/// <summary>
/// Grain-scoped component that owns the timer, reminder, and postman dispatch
/// lifecycle for draining an <see cref="Outbox{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Architecture:</b> Each grain with an outbox gets its own
/// <see cref="OutboxProcessor{TOutbox}"/> — no external scan, no registry,
/// no second store.
/// <list type="bullet">
/// <item><b>GrainTimer</b> for in-process fast retry while activated.</item>
/// <item><b>Durable Reminder</b> for cross-activation recovery. Reactivates
/// the grain if it deactivates with pending items.</item>
/// </list>
/// </para>
/// <para>
/// <b>Non-reentrant assumption:</b> Relies on Orleans default turn-based
/// concurrency — adds no internal locks. <b>Not safe</b> on
/// <c>[Reentrant]</c> grains. Document and enforce via code review.
/// </para>
/// <para>
/// <b>Postman dispatch:</b> The grain registers one or more postmen via
/// <see cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>, each handling a
/// subtype of <typeparamref name="TOutbox"/>. Matching is
/// <b>first-registered-wins</b> against the item's runtime type — register
/// from most specific to least specific (like a <c>switch</c>).
/// <list type="bullet">
/// <item>Each item dispatches to exactly <b>one</b> postman.</item>
/// <item>Items whose runtime type matches no postman → reported as failed
/// with <see cref="NoPostmanRegisteredException"/> via
/// <see cref="OutboxProcessorOptions{TOutbox}.OnPostErrorAsync"/>.</item>
/// <item>Per-item exceptions are caught and surfaced through
/// <see cref="OutboxProcessorOptions{TOutbox}.OnPostErrorAsync"/> with
/// an in-memory attempt count that resets on grain reactivation.</item>
/// </list>
/// </para>
/// <para>
/// <b><see cref="PostAsync"/> error contract:</b> Only throws
/// <see cref="TimeoutException"/> (per-run timeout),
/// <see cref="OperationCanceledException"/> (caller token), or callback
/// exceptions from <see cref="OutboxProcessorOptions{TOutbox}.OnPostCompletedAsync"/>
/// / <see cref="OutboxProcessorOptions{TOutbox}.OnPostErrorAsync"/>. Per-item
/// postman failures are swallowed and routed to the error callback.
/// </para>
/// <para>
/// <b>Grain integration:</b> Two obligations (both compiler-enforced):
/// <list type="number">
/// <item>Implement <see cref="IOutboxGrain"/>.</item>
/// <item>Call <c>RegisterOutboxProcessor(...)</c> in <c>OnActivateAsync</c>.</item>
/// </list>
/// No <c>ReceiveReminder</c> override needed — the <see cref="IOutboxGrain"/>
/// DIM handles it. No manual timer/reminder lifecycle. No telemetry wiring.
/// </para>
/// <para>
/// <b>Escape hatch:</b> Grains with their own reminders can forward unknown
/// reminder names to <see cref="ReceiveReminderAsync"/>:
/// <code>
/// public async Task ReceiveReminder(string name, TickStatus status)
/// {
///     if (name == MyOwnReminder) { await DoMyWork(); return; }
///     await outboxProcessor.ReceiveReminderAsync(name, status);
/// }
/// </code>
/// </para>
/// <para>
/// <b>Telemetry:</b> Emits to meter <c>egil.orleans.messaging</c>:
/// <c>outbox.post.duration</c> (histogram), <c>outbox.post.item.duration</c>
/// (histogram), <c>outbox.post.items</c> (counter), <c>outbox.post.errors</c>
/// (counter), <c>outbox.depth</c> (gauge). Tags: <c>grain.type</c>,
/// <c>event.type</c>, <c>success</c>.
/// </para>
/// </remarks>
/// <typeparam name="TOutbox">
/// The base type of items in the outbox. Postmen can handle subtypes via
/// <see cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>.
/// </typeparam>
public sealed partial class OutboxProcessor<TOutbox> : IOutboxComponent
    where TOutbox : notnull
{
    internal OutboxProcessor() { }

    /// <summary>
    /// Registers a postman that handles items of type <typeparamref name="TSub"/>.
    /// </summary>
    /// <remarks>
    /// <b>Order matters.</b> Postmen are matched first-registered-wins against
    /// the item's runtime type. Register from most specific to least specific.
    /// Returns <c>this</c> for fluent chaining during <c>OnActivateAsync</c>.
    /// </remarks>
    /// <typeparam name="TSub">The subtype this postman handles.</typeparam>
    /// <param name="postman">
    /// Async callback that delivers the item. Per-item exceptions are caught
    /// and surfaced through <see cref="OutboxProcessorOptions{TOutbox}.OnPostErrorAsync"/>.
    /// </param>
    /// <returns>This processor for chaining.</returns>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, ValueTask> postman) where TSub : TOutbox
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, Task> postman) where TSub : TOutbox
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>
    /// <param name="postman">
    /// Async callback with cancellation support. The token is the per-run
    /// timeout from <see cref="OutboxProcessorOptions{TOutbox}.ProcessingTimeout"/>.
    /// </param>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, CancellationToken, Task> postman) where TSub : TOutbox
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Posts all pending items by dispatching each to its matching postman.
    /// Arms timer/reminder if items remain; unregisters if the outbox is empty.
    /// </summary>
    /// <remarks>
    /// Safe to call from the grain's task scheduler (turn-based). Does not
    /// throw for per-item postman failures — those are routed to
    /// <see cref="OutboxProcessorOptions{TOutbox}.OnPostErrorAsync"/>. Only
    /// throws <see cref="TimeoutException"/>,
    /// <see cref="OperationCanceledException"/>, or callback exceptions.
    /// </remarks>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public ValueTask PostAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Called by the <see cref="IOutboxGrain"/> DIM when a reminder fires.
    /// No-ops for reminder names not owned by this processor.
    /// </summary>
    /// <param name="reminderName">The reminder name from Orleans.</param>
    /// <param name="status">The tick status from Orleans.</param>
    public ValueTask ReceiveReminderAsync(string reminderName, TickStatus status)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Attaches this processor to the grain's <see cref="IGrainContext"/> as
    /// a component so the <see cref="IOutboxGrain"/> DIM can discover it.
    /// Called internally by <c>RegisterOutboxProcessor</c>.
    /// </summary>
    internal void AttachToGrain()
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Internal interface registered as a grain context component so the
/// <see cref="IOutboxGrain"/> DIM can forward reminder callbacks without
/// knowing the outbox generic type.
/// </summary>
internal interface IOutboxComponent
{
    /// <summary>Forwards a reminder callback to the outbox processor.</summary>
    ValueTask ReceiveReminderAsync(string reminderName, TickStatus status);
}
