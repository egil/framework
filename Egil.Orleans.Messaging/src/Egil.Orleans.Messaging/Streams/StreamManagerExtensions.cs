using Egil.Orleans.Messaging.Streams;
using Egil.Orleans.Messaging.Tracking;

namespace Orleans;

/// <summary>
/// Extension methods for registering <see cref="StreamManager"/> into a grain's
/// activation lifecycle.
/// </summary>
public static class StreamManagerExtensions
{
    extension<TGrain>(TGrain grain)
        where TGrain : IGrainBase
    {
        /// <summary>
        /// Registers a <see cref="StreamManager"/> for the grain and returns it
        /// for fluent stream subscription configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Call from the grain constructor, then chain
        /// <c>ConfigureImplicitSubscription</c> or
        /// <c>ConfigureExplicitSubscription</c> calls on the returned manager.
        /// </para>
        /// <para>
        /// <b>Usage:</b>
        /// <code>
        /// public override async Task OnActivateAsync(CancellationToken ct)
        /// {
        ///     streamManager = this.RegisterStreamManager(() => state.Tracker)
        ///         .ConfigureExplicitSubscription("StreamProvider", "electricity-prices", HandlePriceTickAsync);
        ///     await streamManager.EnsureExplicitSubscriptionsAsync(ct);
        /// }
        /// </code>
        /// </para>
        /// </remarks>
        /// <param name="getTracker">
        /// Optional accessor for the grain's <see cref="MessageTracker"/> after
        /// hydration. Evaluated when subscriptions look up resume tokens.
        /// Omit it when the grain does not persist stream tracking state.
        /// </param>
        /// <returns>
        /// The registered <see cref="StreamManager"/>. Store it in a grain field so
        /// configured subscriptions remain rooted for the activation lifetime.
        /// </returns>
        public StreamManager RegisterStreamManager(Func<MessageTracker?>? getTracker = null)
        {
            return StreamManager.Create(grain, getTracker);
        }
    }
}