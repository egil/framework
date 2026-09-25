using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Scheduling settings for <see cref="OutboxProcessor{TOutbox}"/> that do not
/// depend on the outbox payload type, so a silo can set them once for every
/// processor.
/// </summary>
/// <remarks>
/// <para>
/// Every processor starts from the silo-wide defaults registered with
/// <c>ConfigureOutboxProcessor</c> on the silo builder, or with
/// <c>services.Configure&lt;OutboxProcessorOptions&gt;(...)</c>. The
/// <c>configure</c> callback passed to <c>RegisterOutboxProcessor</c> then
/// receives an <see cref="OutboxProcessorOptions{TOutbox}"/> holding those
/// defaults and adds the acknowledgement callbacks and any overrides.
/// </para>
/// <para>
/// The processor reads the settings once, when the callback returns. Changing
/// the instance afterwards has no effect.
/// </para>
/// </remarks>
public class OutboxProcessorOptions
{
    /// <summary>
    /// Maximum time per post run. Default: 20 seconds.
    /// </summary>
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Clock used to enforce <see cref="ProcessingTimeout"/>. Default:
    /// <see langword="null"/>, which uses the <see cref="System.TimeProvider"/>
    /// registered in the silo's services, or <see cref="TimeProvider.System"/>
    /// when none is registered.
    /// </summary>
    /// <remarks>
    /// This provider controls only the processing timeout. Orleans owns the
    /// grain timers and reminders used for <see cref="RetryDelay"/>. Set a
    /// shared domain clock once for the silo with the
    /// <c>ConfigureOutboxProcessor</c> overload that receives the
    /// <see cref="IServiceProvider"/>.
    /// </remarks>
    public TimeProvider? TimeProvider { get; set; }

    /// <summary>
    /// Delay before retrying remaining pending items. Cross-activation retry
    /// is clamped to at least one minute because Orleans reminders do not
    /// support sub-minute precision. Default: 2 minutes.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Whether background posting may allow other grain calls to run while
    /// postmen are awaiting asynchronous work.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="true"/> so slow delivery does not block
    /// unrelated calls to the grain. This controls the delivery phase only;
    /// the acknowledgement callbacks use
    /// <see cref="InterleaveAcknowledgementCallbacks"/>. Snapshot reads from
    /// the outbox accessor can also happen after a background delivery pass to
    /// decide whether retry work remains.
    /// </remarks>
    public bool Interleave { get; set; } = true;

    /// <summary>
    /// Whether the acknowledgement callbacks may interleave with other grain
    /// calls when posting runs in the background.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/> because these callbacks usually
    /// update durable outbox state. Orleans reentrancy rules still apply:
    /// reentrant grains may interleave these callbacks regardless.
    /// </remarks>
    public bool InterleaveAcknowledgementCallbacks { get; set; }

    /// <summary>
    /// Whether background retry work should keep the grain activation alive
    /// while pending outbox items remain.
    /// </summary>
    public bool KeepAlive { get; set; }

    internal void CopyTo(OutboxProcessorOptions target)
    {
        target.ProcessingTimeout = ProcessingTimeout;
        target.TimeProvider = TimeProvider;
        target.RetryDelay = RetryDelay;
        target.Interleave = Interleave;
        target.InterleaveAcknowledgementCallbacks = InterleaveAcknowledgementCallbacks;
        target.KeepAlive = KeepAlive;
    }
}

/// <summary>
/// Configuration for one <see cref="OutboxProcessor{TOutbox}"/>: the shared
/// scheduling settings from <see cref="OutboxProcessorOptions"/> plus the
/// acknowledgement callbacks for this grain's outbox. At least one of
/// <see cref="AcknowledgePosted"/> or <see cref="AcknowledgePostedAsync"/> must
/// be configured; when both are set, both run in that order.
/// </summary>
/// <typeparam name="TOutbox">
/// The base payload type of outbox messages. Must match the type parameter of the
/// <see cref="OutboxProcessor{TOutbox}"/> this options instance configures.
/// </typeparam>
public sealed class OutboxProcessorOptions<TOutbox> : OutboxProcessorOptions
    where TOutbox : notnull
{
    internal OutboxProcessorOptions()
    {
    }

    /// <summary>
    /// Called synchronously with items that were successfully dispatched by their postmen.
    /// The grain is expected to remove these items from its outbox.
    /// </summary>
    /// <remarks>
    /// When both acknowledgement callbacks are configured, this callback runs before
    /// <see cref="AcknowledgePostedAsync"/>.
    /// </remarks>
    public Action<ImmutableArray<OutboxMessageEnvelope<TOutbox>>>? AcknowledgePosted { get; set; }

    /// <summary>
    /// Called asynchronously with items that were successfully dispatched by their postmen.
    /// The grain is expected to remove these items from its outbox.
    /// </summary>
    /// <remarks>
    /// When both acknowledgement callbacks are configured, this callback runs after
    /// <see cref="AcknowledgePosted"/>.
    ///
    /// <para>
    /// Persisting the removal immediately is the straightforward choice, but not the
    /// only one: because an item only leaves the <em>durable</em> outbox once the
    /// removal is written, deferring that write risks a redelivery and never a lost
    /// message. A grain using <c>IStateManager&lt;T&gt;</c> can therefore
    /// assign <c>State</c> and let the next business write carry it. If it does,
    /// note that this processor reconciles its retry timer and reminder against the
    /// outbox accessor's snapshot, which reflects the deferred removal. Retry
    /// is therefore disabled on the strength of a removal that is not durable yet, and
    /// anything that later discards the change brings those items back as pending without
    /// re-arming it: a write that fails, a successful <c>ReadAsync</c> or
    /// <c>ClearAsync</c>, which let storage win, or — in a <c>[Reentrant]</c> grain or
    /// with interleaved acknowledgement — a business write that was already in flight when
    /// the assignment happened and finishes by adopting its own value. Post again in any of
    /// those cases.
    /// </para>
    /// The batch contains exactly the items that posted successfully and is
    /// <em>not necessarily a contiguous prefix</em> of the outbox accessor's
    /// snapshot: different postmen dispatch their
    /// groups concurrently, and an item without a matching postman fails in
    /// place while later items can still succeed. Remove the received items
    /// by their <see cref="OutboxMessageEnvelope{T}.Id"/>,
    /// never by position or count — positional removal can drop a failed,
    /// undelivered item and lose it.
    /// </remarks>
    public Func<ImmutableArray<OutboxMessageEnvelope<TOutbox>>, CancellationToken, ValueTask>? AcknowledgePostedAsync { get; set; }

    /// <summary>
    /// Called with items that failed dispatch, along with the exception and
    /// the in-memory attempt count. The grain decides whether to leave them
    /// pending, remove them, or move them to dead-letter state.
    /// </summary>
    /// <remarks>
    /// Attempt counts are tracked in memory only, keyed by item equality:
    /// they reset to one when the grain activation recycles, and item types
    /// without stable value equality (for example mutable classes mutated
    /// after enqueue) make counts restart silently. Counters are pruned during
    /// retry-state reconciliation, once the item is no longer pending. Policies that must
    /// survive activation restarts (max attempts before dead-letter, etc.)
    /// should persist their own counters on the items or grain state.
    /// </remarks>
    public Func<ImmutableArray<(OutboxMessageEnvelope<TOutbox> Item, Exception Error, int Attempt)>, CancellationToken, ValueTask>? AcknowledgeFailuresAsync { get; set; }

    internal static OutboxProcessorOptions<TOutbox> Resolve(
        IServiceProvider services,
        Action<OutboxProcessorOptions<TOutbox>> configure,
        string paramName)
    {
        var options = new OutboxProcessorOptions<TOutbox>();
        services.GetService<IOptionsFactory<OutboxProcessorOptions>>()?
            .Create(Options.DefaultName)
            .CopyTo(options);
        configure(options);

        var snapshot = options.Snapshot();
        snapshot.TimeProvider ??= services.GetService<TimeProvider>() ?? TimeProvider.System;
        snapshot.Validate(paramName);
        return snapshot;
    }

    private OutboxProcessorOptions<TOutbox> Snapshot()
    {
        var snapshot = new OutboxProcessorOptions<TOutbox>
        {
            AcknowledgePosted = AcknowledgePosted,
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            AcknowledgeFailuresAsync = AcknowledgeFailuresAsync,
        };
        CopyTo(snapshot);
        return snapshot;
    }

    internal void Validate(string paramName)
    {

        if (AcknowledgePosted is null && AcknowledgePostedAsync is null)
        {
            throw new ArgumentException("At least one of AcknowledgePosted or AcknowledgePostedAsync must be configured.", paramName);
        }

        if (ProcessingTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(paramName, "ProcessingTimeout must be greater than zero.");
        }

        if (RetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(paramName, "RetryDelay must be greater than zero.");
        }
    }
}
