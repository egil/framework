using Microsoft.Extensions.DependencyInjection;
using Egil.Orleans.Messaging.Streams;
using Egil.Orleans.Messaging.Streams.EventHubs;
using Orleans.Serialization;

namespace Orleans.Hosting;

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
/// for lag measurement and <see cref="StreamCursor.TryGetProviderName"/>
/// for provider-aware dedup and diagnostics.
/// </para>
/// </remarks>
public static class EnrichedEventHubAdapterExtensions
{
    extension(IEventHubStreamConfigurator configurator)
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
    public void UseEnrichedDataAdapter()
    {
        ArgumentNullException.ThrowIfNull(configurator);

        configurator.UseDataAdapter((services, providerName) =>
            new EnrichedEventHubAdapter(
                providerName,
                services.GetRequiredService<Serializer>()));
    }
    }
}
