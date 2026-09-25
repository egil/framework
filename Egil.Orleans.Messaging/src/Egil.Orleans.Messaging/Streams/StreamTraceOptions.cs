namespace Egil.Orleans.Messaging.Streams;

/// <summary>
/// How the <c>orleans.stream.process</c> consumer span relates to the
/// producer's W3C traceparent carried on the stream token.
/// </summary>
public enum StreamTraceMode
{
    /// <summary>
    /// Start the consumer span in a new trace with an <see cref="System.Diagnostics.ActivityLink"/>
    /// to the producer span.
    /// </summary>
    Link,

    /// <summary>
    /// Start the consumer span as a child of the producer span, in the producer's trace.
    /// </summary>
    Parent,

    /// <summary>
    /// Behave as <see cref="Parent"/> when the enqueue lag is at most
    /// <see cref="StreamTraceOptions.MaxParentLag"/>, and as <see cref="Link"/> otherwise
    /// or when the token carries no enqueue time.
    /// </summary>
    ParentWithinLag,
}

/// <summary>
/// Per-subscription choice of how <see cref="StreamManager"/> correlates the
/// consumer span with the producer's trace context.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Link"/> is the default. Each delivery gets its own trace and links
/// back to the producer, so a backlog replayed hours after an outage never
/// stretches a producer's trace across that time.
/// </para>
/// <para>
/// <see cref="Parent"/> puts the consumer span in the producer's trace. Use it
/// for internal grain-to-grain streams with bounded fan-out, where backends
/// that build trees from the trace id, or tail samplers that decide per trace
/// id, would otherwise split one causal flow into separately sampled traces.
/// </para>
/// <para>
/// <see cref="ParentWithinLag(TimeSpan)"/> parents only when the delivery is
/// close in time to the enqueue, and links otherwise. It needs a token that
/// exposes an enqueue time, such as the enriched Event Hubs token.
/// </para>
/// </remarks>
public sealed record StreamTraceOptions
{
    private StreamTraceOptions(StreamTraceMode mode, TimeSpan maxParentLag)
    {
        Mode = mode;
        MaxParentLag = maxParentLag;
    }

    /// <summary>
    /// Links the consumer span to the producer span. This is the default.
    /// </summary>
    public static StreamTraceOptions Link { get; } = new(StreamTraceMode.Link, TimeSpan.Zero);

    /// <summary>
    /// Parents the consumer span to the producer span regardless of lag.
    /// </summary>
    public static StreamTraceOptions Parent { get; } = new(StreamTraceMode.Parent, TimeSpan.Zero);

    /// <summary>
    /// The correlation mode.
    /// </summary>
    public StreamTraceMode Mode { get; }

    /// <summary>
    /// The largest enqueue lag that still parents the consumer span when
    /// <see cref="Mode"/> is <see cref="StreamTraceMode.ParentWithinLag"/>.
    /// <see cref="TimeSpan.Zero"/> for the other modes.
    /// </summary>
    public TimeSpan MaxParentLag { get; }

    /// <summary>
    /// Parents the consumer span to the producer span when the time between
    /// the broker enqueue and delivery is at most <paramref name="maxParentLag"/>,
    /// and links it otherwise.
    /// </summary>
    /// <param name="maxParentLag">The largest lag that still parents. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxParentLag"/> is zero or negative.</exception>
    public static StreamTraceOptions ParentWithinLag(TimeSpan maxParentLag)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxParentLag, TimeSpan.Zero);
        return new(StreamTraceMode.ParentWithinLag, maxParentLag);
    }
}
