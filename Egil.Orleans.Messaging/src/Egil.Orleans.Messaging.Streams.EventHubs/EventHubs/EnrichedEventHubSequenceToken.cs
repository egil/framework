using Orleans.Streaming.EventHubs;
using Egil.Orleans.Messaging.Tracking;

namespace Egil.Orleans.Messaging.Streams.EventHubs;

/// <summary>
/// An <see cref="EventHubSequenceTokenV2"/> subclass that carries the
/// broker-side <see cref="EnqueuedTime"/> and the <see cref="ProviderName"/>,
/// enabling end-to-end lag measurement and provider-aware dedup in
/// <see cref="StreamManager"/> and <see cref="MessageTracker"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registration:</b> Call
/// <see cref="EnrichedEventHubAdapterExtensions.UseEnrichedDataAdapter"/>
/// on the Event Hub stream configurator during silo setup. This registers
/// the library's internal <c>EnrichedEventHubAdapter</c> that overrides
/// <c>GetStreamPosition</c> and <c>GetSequenceToken</c> to produce
/// <see cref="EnrichedEventHubSequenceToken"/> instances automatically.
/// </para>
/// <para>
/// <b>Transparent to StreamManager:</b> The <see cref="StreamManager"/> is
/// unaware of the adapter. The enrichment surfaces through
/// <see cref="StreamCursor.TryGetEnqueuedTime"/> and
/// <see cref="StreamCursor.TryGetProviderName"/> in the user's
/// <c>OnNext</c> handler.
/// </para>
/// <para>
/// <b>Dedup and edge cases:</b> The <see cref="ProviderName"/> embedded
/// in the token enables <see cref="MessageTracker"/> to distinguish messages
/// arriving from different stream providers on the same stream namespace,
/// supporting multi-provider topologies and provider-specific eviction.
/// </para>
/// <para>
/// <b>OTel trace correlation:</b> The <see cref="TraceParent"/> property
/// carries the W3C <c>traceparent</c> header captured at publish time by
/// <see cref="EnrichedEventHubAdapter"/>. On the consumer side,
/// <see cref="StreamManager"/> reads it via
/// <see cref="StreamCursor.TryGetTraceParent"/> and creates an
/// <see cref="System.Diagnostics.ActivityLink"/> — correlating consumer
/// spans to producer spans without multi-hour parent-child traces.
/// </para>
/// <para>
/// <b>Serialization:</b> Inherits Orleans serialization from
/// <see cref="EventHubSequenceTokenV2"/>. The Event Hubs package also
/// registers <see cref="EnrichedEventHubSequenceTokenJsonConverter"/> from
/// <see cref="EnrichedEventHubAdapterExtensions.UseEnrichedDataAdapter"/>, so
/// <see cref="MessageTracker"/> and <see cref="StreamCursor"/> can round-trip
/// this token through System.Text.Json without the core package referencing
/// Event Hubs.
/// </para>
/// </remarks>
[GenerateSerializer]
[Alias(EnrichedEventHubSequenceToken.TypeAlias)]
public class EnrichedEventHubSequenceToken : EventHubSequenceTokenV2, IStreamSequenceTokenMetadata
{
    /// <summary>
    /// Stable Orleans/STJ token discriminator for this token type.
    /// </summary>
    public const string TypeAlias = "egil.orleans.messaging.EnrichedEventHubSequenceToken";

    /// <summary>
    /// The wall-clock time the event was enqueued at the Event Hub broker.
    /// Used by <see cref="StreamCursor.TryGetEnqueuedTime"/> to compute
    /// end-to-end lag: <c>now - EnqueuedTime</c>.
    /// </summary>
    [Id(0)]
    public DateTimeOffset EnqueuedTime { get; }

    /// <summary>
    /// The name of the Orleans stream provider that delivered this event.
    /// Enables provider-aware dedup and diagnostics in <see cref="MessageTracker"/>.
    /// </summary>
    [Id(1)]
    public string ProviderName { get; }

    /// <summary>
    /// The W3C <c>traceparent</c> header value from the producer-side
    /// <see cref="System.Diagnostics.Activity"/> at publish time, or <c>null</c>
    /// if no activity was active when the event was published.
    /// </summary>
    /// <remarks>
    /// Stamped by <see cref="EnrichedEventHubAdapter"/> in its
    /// <c>ToQueueMessage</c> override (producer side) and extracted in
    /// <c>GetStreamPosition</c> (consumer side).
    /// <see cref="StreamManager"/> uses this to create
    /// <see cref="System.Diagnostics.ActivityLink"/>s — correlating consumer
    /// spans to producer spans without creating multi-hour parent-child traces.
    /// </remarks>
    [Id(2)]
    public string? TraceParent { get; }

    /// <summary>
    /// Creates a new enriched token with the broker-side enqueue time and
    /// stream provider name.
    /// </summary>
    /// <param name="offset">The Event Hub partition offset (string).</param>
    /// <param name="sequenceNumber">The Event Hub sequence number.</param>
    /// <param name="eventIndex">
    /// The index of the event within a batch at the same sequence number.
    /// </param>
    /// <param name="enqueuedTime">
    /// The <see cref="DateTimeOffset"/> when the event was enqueued at the
    /// Event Hub broker.
    /// </param>
    /// <param name="providerName">
    /// The name of the Orleans stream provider. Passed through from the
    /// <c>EnrichedEventHubAdapter</c> at construction time.
    /// </param>
    /// <param name="traceParent">
    /// The W3C <c>traceparent</c> value captured at publish time, or
    /// <c>null</c> if no <see cref="System.Diagnostics.Activity"/> was active.
    /// </param>
    public EnrichedEventHubSequenceToken(
        string offset,
        long sequenceNumber,
        int eventIndex,
        DateTimeOffset enqueuedTime,
        string providerName,
        string? traceParent = null)
        : base(offset, sequenceNumber, eventIndex)
    {
        EnqueuedTime = enqueuedTime;
        ProviderName = providerName;
        TraceParent = traceParent;
    }

    /// <summary>
    /// Parameterless constructor for Orleans serializer use only.
    /// </summary>
    public EnrichedEventHubSequenceToken()
    {
        ProviderName = string.Empty;
    }

    /// <inheritdoc/>
    public bool TryGetEnqueuedTime(out DateTimeOffset enqueuedTime)
    {
        enqueuedTime = EnqueuedTime;
        return true;
    }

    /// <inheritdoc/>
    public bool TryGetProviderName([NotNullWhen(true)] out string? providerName)
    {
        providerName = ProviderName;
        return !string.IsNullOrWhiteSpace(providerName);
    }

    /// <inheritdoc/>
    public bool TryGetTraceParent([NotNullWhen(true)] out string? traceParent)
    {
        traceParent = TraceParent;
        return traceParent is not null;
    }
}
