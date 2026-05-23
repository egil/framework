namespace Egil.Orleans.Messaging;

/// <summary>
/// Extension methods for wiring <see cref="OutboxProcessor{TOutbox}"/> into
/// a grain's activation lifecycle.
/// </summary>
public static class OutboxProcessorExtensions
{
    /// <summary>
    /// Creates and attaches an <see cref="OutboxProcessor{TOutbox}"/> to the
    /// grain. Call in <c>OnActivateAsync</c> and chain
    /// <see cref="OutboxProcessor{TOutbox}.AddPostman{TSub}(Func{TSub, ValueTask})"/>
    /// calls on the result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registers the processor as an <see cref="IOutboxComponent"/> on the
    /// grain context so the <see cref="IOutboxGrain"/> DIM can discover it
    /// for reminder forwarding.
    /// </para>
    /// <para>
    /// <b>Usage:</b>
    /// <code>
    /// public override async Task OnActivateAsync(CancellationToken ct)
    /// {
    ///     outboxProcessor = this.InitializeOutboxProcessor(new OutboxProcessorOptions&lt;IMyEvent&gt;
    ///     {
    ///         GetPending = () => stateManager.State.Outbox
    ///             .Select(e => e.Message).ToImmutableArray(),
    ///         OnPostCompletedAsync = async (items, ct) =>
    ///         {
    ///             // remove delivered items and persist
    ///         },
    ///         OnPostErrorAsync = async (failures, ct) =>
    ///         {
    ///             // log, dead-letter, or leave for retry
    ///         },
    ///     })
    ///     .AddPostman&lt;PriceCalculated&gt;(PublishPriceCalculatedAsync)
    ///     .AddPostman&lt;InvoiceReady&gt;(SendInvoiceReadyAsync);
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <typeparam name="TGrain">
    /// The grain type. Must implement <see cref="IOutboxGrain"/> (for reminder
    /// DIM) and <see cref="IGrainBase"/> (for grain context access). Both
    /// constraints are compiler-enforced.
    /// </typeparam>
    /// <typeparam name="TOutbox">The base type of outbox items.</typeparam>
    /// <param name="grain">The grain instance (<c>this</c>).</param>
    /// <param name="options">Processor configuration.</param>
    /// <returns>
    /// The processor instance. Chain <c>AddPostman</c> calls on it, then store
    /// in a grain field for later <see cref="OutboxProcessor{TOutbox}.PostAsync"/>
    /// calls.
    /// </returns>
    public static OutboxProcessor<TOutbox> InitializeOutboxProcessor<TGrain, TOutbox>(
        this TGrain grain,
        OutboxProcessorOptions<TOutbox> options)
        where TGrain : IGrainBase, IOutboxGrain
        where TOutbox : notnull
    {
        throw new NotImplementedException();
    }
}
