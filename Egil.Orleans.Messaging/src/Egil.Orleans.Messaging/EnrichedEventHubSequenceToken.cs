using Orleans.Streaming.EventHubs;

namespace Egil.Orleans.Messaging;

/// <summary>
/// An <see cref="EventHubSequenceTokenV2"/> subclass that carries the
/// broker-side <see cref="EnqueuedTime"/> and the <see cref="StreamProviderName"/>,
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
/// <see cref="StreamCursor.TryGetStreamProviderName"/> in the user's
/// <c>OnNext</c> handler.
/// </para>
/// <para>
/// <b>Dedup and edge cases:</b> The <see cref="StreamProviderName"/> embedded
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
/// <see cref="EventHubSequenceTokenV2"/>. The library's STJ converter for
/// <see cref="StreamCursor"/> recognizes this subtype via a <c>$kind</c>
/// discriminator and round-trips <see cref="EnqueuedTime"/>,
/// <see cref="StreamProviderName"/>, and <see cref="TraceParent"/> fields.
/// </para>
/// </remarks>
[GenerateSerializer]
[Alias("egil.orleans.messaging.EnrichedEventHubSequenceToken")]
public class EnrichedEventHubSequenceToken : EventHubSequenceTokenV2
{
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
    public string StreamProviderName { get; }

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
    /// <param name="streamProviderName">
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
        string streamProviderName,
        string? traceParent = null)
        : base(offset, sequenceNumber, eventIndex)
    {
        EnqueuedTime = enqueuedTime;
        StreamProviderName = streamProviderName;
        TraceParent = traceParent;
    }

    /// <summary>
    /// Parameterless constructor for Orleans serializer use only.
    /// </summary>
    public EnrichedEventHubSequenceToken()
    {
        StreamProviderName = string.Empty;
    }
}
