using System.Diagnostics;
using Orleans.Providers.Streams.Common;
using Orleans.Serialization;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Streams.EventHubs;

/// <summary>
/// An <see cref="EventHubDataAdapter"/> subclass that produces
/// <see cref="EnrichedEventHubSequenceToken"/> instances carrying the
/// broker-side enqueue time, stream provider name, and W3C traceparent.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registration:</b> Do not instantiate directly. Use
/// <see cref="EnrichedEventHubAdapterExtensions.UseEnrichedDataAdapter"/>
/// on the <c>IEventHubStreamConfigurator</c> during silo setup:
/// <code>
/// siloBuilder.AddEventHubStreams("my-provider", b =>
/// {
///     b.UseEnrichedDataAdapter();
///     // ... other config
/// });
/// </code>
/// </para>
/// <para>
/// <b>Overrides:</b> This adapter overrides the Event Hubs conversion points
/// that create messages and sequence tokens:
/// <list type="bullet">
/// <item><see cref="EventHubDataAdapter.GetStreamPosition"/> and
/// <see cref="EventHubDataAdapter.GetSequenceToken(ref CachedMessage)"/>
/// — to return <see cref="EnrichedEventHubSequenceToken"/> instead of
/// <see cref="EventHubSequenceTokenV2"/>.</item>
/// <item><c>GetBatchContainer(EventHubMessage)</c> — to expose enriched
/// per-event tokens to stream consumers while preserving the base batch
/// container behavior. It is sealed; subclasses that need a custom container
/// override <see cref="CreateInnerBatchContainer"/> instead.</item>
/// <item><see cref="ToQueueMessage{T}"/> — to stamp
/// <c>Activity.Current?.Id</c> into the outgoing
/// <c>EventData.Properties["traceparent"]</c> before the event hits
/// Event Hub, enabling cross-queue trace correlation on the consumer
/// side.</item>
/// </list>
/// All other adapter behavior, including partition-key selection, is inherited
/// unchanged from <see cref="EventHubDataAdapter"/>.
/// </para>
/// <para>
/// <b>OTel trace correlation:</b> Orleans streams lose
/// <see cref="Activity.Current"/> across the queue boundary. This adapter
/// uses the W3C <c>traceparent</c> propagation pattern:
/// <list type="number">
/// <item><b>Producer side</b> (<see cref="ToQueueMessage{T}"/>):
/// stashes <c>Activity.Current?.Id</c> into
/// <c>EventData.Properties["traceparent"]</c>.</item>
/// <item><b>Consumer side</b> (<c>GetBatchContainer(EventHubMessage)</c>):
/// extracts the <c>traceparent</c> property and stores it in each delivered
/// <see cref="EnrichedEventHubSequenceToken.TraceParent"/>.</item>
/// <item><b>StreamManager</b>: reads
/// <see cref="StreamCursor.TryGetTraceParent"/> and, by default, creates an
/// <see cref="ActivityLink"/> from the consumer span to the producer span.
/// A subscription configured with <see cref="StreamTraceOptions"/> can parent
/// the consumer span to the producer span instead.</item>
/// </list>
/// </para>
/// <para>
/// <b>Subclassing:</b> This class is not sealed — users who need additional
/// adapter customization (custom partition keys, custom batch containers,
/// etc.) can inherit from <see cref="EnrichedEventHubAdapter"/> instead of
/// from <see cref="EventHubDataAdapter"/> directly. This preserves the
/// enriched token behavior while allowing further overrides:
/// <code>
/// public class MyAdapter : EnrichedEventHubAdapter
/// {
///     public MyAdapter(string providerName, Serializer serializer)
///         : base(providerName, serializer) { }
///
///     public override string GetPartitionKey(StreamId streamId)
///         => streamId.GetNamespace();
///
///     protected override IBatchContainer CreateInnerBatchContainer(EventHubMessage message)
///         => new MyBatchContainer(message);
/// }
/// </code>
/// When subclassing, register via <c>UseDataAdapter</c> directly on the
/// <c>IEventHubStreamConfigurator</c> with your custom adapter type.
/// </para>
/// </remarks>
public class EnrichedEventHubAdapter : EventHubDataAdapter
{
    /// <summary>
    /// The Event Hub application property key used to propagate the W3C
    /// <c>traceparent</c> header across the queue boundary.
    /// </summary>
    protected const string TraceParentPropertyKey = "traceparent";

    /// <summary>
    /// The name of the Orleans stream provider. Accessible to subclasses for
    /// custom token construction or diagnostics.
    /// </summary>
    protected string ProviderName { get; }

    /// <summary>
    /// Creates a new adapter that enriches tokens with the given
    /// <paramref name="providerName"/>.
    /// </summary>
    /// <param name="providerName">
    /// The name of the Orleans stream provider, passed through from the
    /// <c>UseDataAdapter</c> factory delegate. Stored in every
    /// <see cref="EnrichedEventHubSequenceToken"/> produced by this adapter.
    /// </param>
    /// <param name="serializer">
    /// The Orleans <see cref="Serializer"/> used by the base
    /// <see cref="EventHubDataAdapter"/> for batch container
    /// serialization/deserialization.
    /// </param>
    public EnrichedEventHubAdapter(string providerName, Serializer serializer)
        : base(serializer)
    {
        ProviderName = providerName;
    }

    /// <summary>
    /// Overrides the base to stamp <c>Activity.Current?.Id</c> into the
    /// outgoing <c>EventData.Properties["traceparent"]</c> before the event
    /// is published to Event Hub.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the <b>producer side</b> of the OTel trace correlation pattern.
    /// The current <see cref="Activity.Id"/> (W3C format) is captured at
    /// publish time and stored as an Event Hub application property. On the
    /// consumer side, the batch-container conversion extracts it into each
    /// delivered <see cref="EnrichedEventHubSequenceToken.TraceParent"/>.
    /// </para>
    /// <para>
    /// If no <see cref="Activity"/> is active at publish time, no property
    /// is set and the consumer-side token will have a <c>null</c>
    /// <see cref="EnrichedEventHubSequenceToken.TraceParent"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The type of stream events.</typeparam>
    /// <param name="streamId">The target stream identity.</param>
    /// <param name="events">The events to publish.</param>
    /// <param name="token">The sequence token; Event Hubs always passes <c>null</c>.</param>
    /// <param name="requestContext">The Orleans request context dictionary, or <c>null</c> when no request context is flowing.</param>
    /// <returns>
    /// An <see cref="Azure.Messaging.EventHubs.EventData"/> with the
    /// <c>traceparent</c> property stamped if an <see cref="Activity"/>
    /// is active.
    /// </returns>
    public override Azure.Messaging.EventHubs.EventData ToQueueMessage<T>(
        StreamId streamId,
        IEnumerable<T> events,
        StreamSequenceToken? token,
        Dictionary<string, object>? requestContext)
    {
        var queueMessage = base.ToQueueMessage(streamId, events, token, requestContext);
        if (Activity.Current?.Id is { } traceParent)
        {
            queueMessage.Properties[TraceParentPropertyKey] = traceParent;
        }

        return queueMessage;
    }

    /// <summary>
    /// Overrides the base to produce an <see cref="EnrichedEventHubSequenceToken"/>
    /// carrying the cached message's enqueue time and the stream provider name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the Event Hub cache when delivering messages from the in-memory
    /// cache. The <see cref="CachedMessage.EnqueueTimeUtc"/> is available on every
    /// cached message — no additional broker round-trip is needed.
    /// </para>
    /// <para>
    /// <b>Note:</b> This cache-cursor token cannot include
    /// <see cref="EnrichedEventHubSequenceToken.TraceParent"/> because
    /// <see cref="CachedMessage"/> does not expose Event Hub application
    /// properties. Consumer delivery reconstructs those properties and adds
    /// the traceparent through <c>GetBatchContainer(EventHubMessage)</c>.
    /// </para>
    /// </remarks>
    /// <param name="cachedMessage">The cached Event Hub message.</param>
    /// <returns>
    /// An <see cref="EnrichedEventHubSequenceToken"/> with the enqueue time and
    /// provider name populated. <see cref="EnrichedEventHubSequenceToken.TraceParent"/>
    /// is <c>null</c> for cache-reconstructed tokens.
    /// </returns>
    public override StreamSequenceToken GetSequenceToken(ref CachedMessage cachedMessage)
    {
        var offset = cachedMessage.Segment.Array is null
            ? string.Empty
            : GetOffset(cachedMessage) ?? string.Empty;

        return new EnrichedEventHubSequenceToken(
            offset,
            cachedMessage.SequenceNumber,
            cachedMessage.EventIndex,
            NormalizeUtc(cachedMessage.EnqueueTimeUtc),
            ProviderName);
    }

    /// <summary>
    /// Wraps the inner batch container so the tokens observed by stream
    /// consumers carry the Event Hub metadata reconstructed from cache.
    /// </summary>
    /// <remarks>
    /// Sealed so enrichment can never be lost: the wrapper that carries the
    /// <see cref="EnrichedEventHubSequenceToken"/> is internal, so a subclass
    /// that replaced this method could not reattach the token. Subclasses
    /// supply their own container through
    /// <see cref="CreateInnerBatchContainer"/> instead, and the adapter keeps
    /// ownership of the wrapping.
    /// </remarks>
    protected sealed override IBatchContainer GetBatchContainer(EventHubMessage eventHubMessage)
    {
        var inner = CreateInnerBatchContainer(eventHubMessage);
        var traceParent = eventHubMessage.Properties.TryGetValue(TraceParentPropertyKey, out var value)
            ? value as string ?? value?.ToString()
            : null;

        // A custom inner container is typically built straight from the EventHubMessage and
        // carries no token of its own, so the batch-level index falls back to 0 rather than
        // forcing the subclass to fabricate a throwaway token just to avoid a null reference.
        var sequenceToken = new EnrichedEventHubSequenceToken(
            eventHubMessage.Offset,
            eventHubMessage.SequenceNumber,
            inner.SequenceToken?.EventIndex ?? 0,
            NormalizeUtc(eventHubMessage.EnqueueTimeUtc),
            ProviderName,
            traceParent);

        return new EnrichedEventHubBatchContainer(inner, sequenceToken);
    }

    /// <summary>
    /// Creates the batch container that decodes the Event Hub payload. Override
    /// this — not <c>GetBatchContainer</c> — to deliver a custom container while
    /// keeping enriched sequence tokens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned container is wrapped by the adapter, which replaces the
    /// batch token and every per-event token with an
    /// <see cref="EnrichedEventHubSequenceToken"/> carrying the Event Hub
    /// offset, sequence number, enqueue time, <see cref="ProviderName"/>, and
    /// the producer-side traceparent. A custom container therefore does not
    /// need to produce sequence tokens at all; returning <c>null</c> tokens is
    /// supported, and per-event indexes then follow enumeration order.
    /// </para>
    /// <para>
    /// The container is serialized to stream consumers through the wrapper's
    /// <see cref="IBatchContainer"/> field, so a custom type must be
    /// <c>[GenerateSerializer]</c> and reachable by the silo's Orleans
    /// serializer.
    /// </para>
    /// </remarks>
    /// <param name="eventHubMessage">
    /// The Event Hub message reconstructed from the cache, including its
    /// application properties.
    /// </param>
    /// <returns>
    /// The container whose payload and request context are delivered to
    /// consumers. The default returns the base
    /// <see cref="EventHubDataAdapter"/> container.
    /// </returns>
    protected virtual IBatchContainer CreateInnerBatchContainer(EventHubMessage eventHubMessage)
        => base.GetBatchContainer(eventHubMessage);

    /// <summary>
    /// Overrides the base to produce an <see cref="EnrichedEventHubSequenceToken"/>
    /// when the adapter first sees a raw <c>EventData</c> from the Event Hub
    /// partition receiver.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called once per incoming <c>EventData</c> before it enters the cache.
    /// The <c>EventData.EnqueuedTime</c> property carries the broker-stamped
    /// UTC time; the <see cref="ProviderName"/> is captured at adapter
    /// construction time.
    /// </para>
    /// <para>
    /// <b>Trace correlation:</b> If the incoming <c>EventData</c> has a
    /// <c>"traceparent"</c> application property (stamped by
    /// <see cref="ToQueueMessage{T}"/> on the producer side), it is extracted
    /// and stored in <see cref="EnrichedEventHubSequenceToken.TraceParent"/>.
    /// </para>
    /// </remarks>
    /// <param name="partition">The Event Hub partition identifier.</param>
    /// <param name="queueMessage">The raw Event Hub message.</param>
    /// <returns>
    /// A <see cref="StreamPosition"/> whose <c>SequenceToken</c> is an
    /// <see cref="EnrichedEventHubSequenceToken"/> with enqueue time,
    /// provider name, and traceparent (if present).
    /// </returns>
    public override StreamPosition GetStreamPosition(
        string partition,
        Azure.Messaging.EventHubs.EventData queueMessage)
    {
        var streamPosition = base.GetStreamPosition(partition, queueMessage);
        var traceParent = queueMessage.Properties.TryGetValue(TraceParentPropertyKey, out var value)
            ? value as string ?? value?.ToString()
            : null;

        var (offset, sequenceNumber, eventIndex) = streamPosition.SequenceToken switch
        {
            EventHubSequenceTokenV2 token => (token.EventHubOffset, token.SequenceNumber, token.EventIndex),
            EventHubSequenceToken token => (token.EventHubOffset, token.SequenceNumber, token.EventIndex),
            _ => (queueMessage.OffsetString ?? string.Empty, queueMessage.SequenceNumber, 0)
        };

        var enrichedToken = new EnrichedEventHubSequenceToken(
            offset ?? string.Empty,
            sequenceNumber,
            eventIndex,
            queueMessage.EnqueuedTime,
            ProviderName,
            traceParent);

        return new StreamPosition(streamPosition.StreamId, enrichedToken);
    }

    private static DateTimeOffset NormalizeUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => value
        };

        return new DateTimeOffset(utc);
    }
}
