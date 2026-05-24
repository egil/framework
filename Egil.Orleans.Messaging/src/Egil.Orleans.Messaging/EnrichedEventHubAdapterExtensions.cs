using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace Egil.Orleans.Messaging;

/// <summary>
/// Extension methods for registering the <see cref="EnrichedEventHubSequenceToken"/>-producing
/// data adapter on an Event Hub stream configurator.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage:</b>
/// <code>
/// siloBuilder.AddEventHubStreams("my-provider", b =>
/// {
///     b.ConfigureEventHub(ob => ob.Configure(options =>
///     {
///         options.ConfigureTableStorageCheckpointing(...);
///     }));
///     b.UseEnrichedDataAdapter();
/// });
/// </code>
/// </para>
/// <para>
/// <b>What it does:</b> Registers the library's internal
/// <c>EnrichedEventHubAdapter</c> via <c>UseDataAdapter</c>. The adapter
/// overrides <c>GetStreamPosition</c> and <c>GetSequenceToken</c> to return
/// <see cref="EnrichedEventHubSequenceToken"/> instances carrying the
/// broker-side enqueue time and the stream provider name. No user-written
/// adapter code is needed.
/// </para>
/// <para>
/// <b>Downstream access:</b> Once registered, the enrichment is transparently
/// available on every stream token. Use <see cref="StreamCursor.TryGetEnqueuedTime"/>
/// for lag measurement and <see cref="StreamCursor.TryGetStreamProviderName"/>
/// for provider-aware dedup and diagnostics.
/// </para>
/// </remarks>
public static class EnrichedEventHubAdapterExtensions
{
    /// <summary>
    /// Registers the library's <see cref="EnrichedEventHubSequenceToken"/>-producing
    /// data adapter on the given Event Hub stream configurator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replaces the default <c>EventHubDataAdapter</c>. Only one data adapter
    /// can be active per stream provider — calling this after a prior
    /// <c>UseDataAdapter</c> replaces the previous registration.
    /// </para>
    /// <para>
    /// The adapter is resolved per-provider: the <c>Serializer</c> is obtained
    /// from the silo's <see cref="IServiceProvider"/>, and the stream provider
    /// name is captured from the configurator's factory delegate.
    /// </para>
    /// </remarks>
    /// <param name="configurator">
    /// The Event Hub stream configurator, obtained from
    /// <c>ISiloBuilder.AddEventHubStreams</c>.
    /// </param>
    public static void UseEnrichedDataAdapter(
        this IEventHubStreamConfigurator configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);

        configurator.UseDataAdapter((services, streamProviderName) =>
            new EnrichedEventHubAdapter(
                streamProviderName,
                services.GetRequiredService<Serializer>()));
    }
}
