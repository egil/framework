namespace Egil.Orleans.Messaging;

/// <summary>
/// Extension methods for wiring <see cref="StreamManager"/> into a grain's
/// activation lifecycle.
/// </summary>
public static class StreamManagerExtensions
{
    /// <summary>
    /// Creates a <see cref="StreamManager"/> for the grain and returns it for
    /// fluent stream subscription configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call from <c>OnActivateAsync</c>, then chain
    /// <c>Subscribe</c> calls on the returned manager.
    /// </para>
    /// <para>
    /// <b>Usage:</b>
    /// <code>
    /// public override async Task OnActivateAsync(CancellationToken ct)
    /// {
    ///     streamManager = this.InitializeStreamManager(state.Tracker)
    ///         .Subscribe("electricity-prices", HandlePriceTickAsync);
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <typeparam name="TGrain">
    /// The grain type. Must implement <see cref="IGrainBase"/> so stream providers
    /// and telemetry infrastructure can be resolved through activation services.
    /// </typeparam>
    /// <param name="grain">The grain instance (<c>this</c>).</param>
    /// <param name="trackerSnapshot">
    /// A snapshot of the grain's <see cref="MessageTracker"/> at activation
    /// time. Used to look up resume tokens.
    /// </param>
    /// <returns>
    /// The initialized <see cref="StreamManager"/>. Store it in a grain field so
    /// the subscriptions remain rooted for the activation lifetime.
    /// </returns>
    public static StreamManager InitializeStreamManager<TGrain>(
        this TGrain grain,
        MessageTracker trackerSnapshot)
        where TGrain : IGrainBase
    {
        return StreamManager.Create(grain, trackerSnapshot);
    }
}
