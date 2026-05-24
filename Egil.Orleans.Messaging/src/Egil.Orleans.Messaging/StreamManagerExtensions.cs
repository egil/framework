namespace Egil.Orleans.Messaging;

/// <summary>
/// Extension methods for registering <see cref="StreamManager"/> into a grain's
/// activation lifecycle.
/// </summary>
public static class StreamManagerExtensions
{
    /// <summary>
    /// Registers a <see cref="StreamManager"/> for the grain and returns it
    /// for fluent stream subscription configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call from <c>OnActivateAsync</c>, chain <c>AddSubscription</c> calls
    /// on the returned manager, then await <c>SubscribeAsync</c>.
    /// </para>
    /// <para>
    /// <b>Usage:</b>
    /// <code>
/// public override async Task OnActivateAsync(CancellationToken ct)
/// {
///     streamManager = this.RegisterStreamManager(state.Tracker)
///         .AddSubscription("electricity-prices", HandlePriceTickAsync);
///     await streamManager.SubscribeAsync(ct);
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
    /// The registered <see cref="StreamManager"/>. Store it in a grain field so
    /// configured subscriptions remain rooted for the activation lifetime.
    /// </returns>
    public static StreamManager RegisterStreamManager<TGrain>(
        this TGrain grain,
        MessageTracker trackerSnapshot)
        where TGrain : IGrainBase
    {
        return StreamManager.Create(grain, trackerSnapshot);
    }
}
