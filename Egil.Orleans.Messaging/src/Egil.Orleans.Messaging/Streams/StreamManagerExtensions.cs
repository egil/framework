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
    /// Call from <c>OnActivateAsync</c>, then chain
    /// <c>ConfigureImplicitSubscription</c> or
    /// <c>ConfigureExplicitSubscription</c> calls on the returned manager.
    /// </para>
    /// <para>
    /// <b>Usage:</b>
    /// <code>
/// public override async Task OnActivateAsync(CancellationToken ct)
/// {
///     streamManager = this.RegisterStreamManager(state.Tracker)
///         .ConfigureExplicitSubscription("StreamProvider", "electricity-prices", HandlePriceTickAsync);
///     await streamManager.EnsureExplicitSubscriptionsAsync(ct);
/// }
/// </code>
    /// </para>
    /// </remarks>
    /// <param name="trackerSnapshot">
    /// A snapshot of the grain's <see cref="MessageTracker"/> at activation
    /// time. Used to look up resume tokens.
    /// </param>
    /// <returns>
    /// The registered <see cref="StreamManager"/>. Store it in a grain field so
    /// configured subscriptions remain rooted for the activation lifetime.
    /// </returns>
    public StreamManager RegisterStreamManager(MessageTracker trackerSnapshot)
    {
        return StreamManager.Create(grain, trackerSnapshot);
    }

    /// <summary>
    /// Registers a <see cref="StreamManager"/> for a grain whose configured
    /// subscriptions all use the same Orleans stream provider.
    /// </summary>
    /// <remarks>
    /// Prefer this overload when provider name and stream namespace are
    /// different. This is the usual shape for grains using
    /// <see cref="ImplicitStreamSubscriptionAttribute"/>, because the attribute
    /// describes namespaces while the provider name is runtime configuration.
    /// </remarks>
    /// <param name="trackerSnapshot">A snapshot of the grain's tracker at activation time.</param>
    /// <param name="streamProviderName">The Orleans stream provider name.</param>
    /// <returns>The registered <see cref="StreamManager"/>.</returns>
    public StreamManager RegisterStreamManager(
        MessageTracker trackerSnapshot,
        string streamProviderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);

        return StreamManager.Create(grain, trackerSnapshot, streamProviderName);
    }
    }
}
