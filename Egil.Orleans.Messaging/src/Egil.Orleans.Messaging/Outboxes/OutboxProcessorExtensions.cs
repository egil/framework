using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Egil.Orleans.Messaging.Outboxes;

namespace Orleans;

/// <summary>
/// Extension methods for wiring <see cref="OutboxProcessor{TOutbox}"/> into
/// a grain's activation lifecycle.
/// </summary>
public static class OutboxProcessorExtensions
{
    extension<TGrain>(TGrain grain)
        where TGrain : IGrainBase, IOutboxGrain
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
    ///     outboxProcessor = this.RegisterOutboxProcessor(new OutboxProcessorOptions&lt;IMyEvent&gt;
    ///     {
    ///         PendingItems = () => stateManager.State.Outbox
    ///             .Select(e => e.Message).ToImmutableArray(),
    ///         AcknowledgePostedAsync = async (items, ct) =>
    ///         {
    ///             // remove delivered items and persist
    ///         },
    ///         ReconcileFailedAsync = async (failures, ct) =>
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
    /// <typeparam name="TOutbox">The base type of outbox items.</typeparam>
    /// <param name="options">Processor configuration.</param>
    /// <returns>
    /// The processor instance. Chain <c>AddPostman</c> calls on it, then store
    /// in a grain field for later <see cref="OutboxProcessor{TOutbox}.PostAsync"/>
    /// calls.
    /// </returns>
    public OutboxProcessor<TOutbox> RegisterOutboxProcessor<TOutbox>(OutboxProcessorOptions<TOutbox> options)
        where TOutbox : notnull
    {
        ArgumentNullException.ThrowIfNull(grain);
        ArgumentNullException.ThrowIfNull(options);

        var services = grain.GrainContext.ActivationServices;
        var processor = new OutboxProcessor<TOutbox>(
            grain,
            services.GetRequiredService<IGrainFactory>(),
            options,
            services.GetRequiredService<ILoggerFactory>().CreateLogger<OutboxProcessor<TOutbox>>());

        processor.AttachToGrain();
        return processor;
    }
    }
}
