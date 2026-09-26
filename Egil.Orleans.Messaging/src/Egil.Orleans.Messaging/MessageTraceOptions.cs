using Egil.Orleans.Messaging.Streams;

namespace Egil.Orleans.Messaging;

/// <summary>
/// How a messaging span relates to the producer's W3C traceparent: the
/// <c>orleans.stream.process</c> consumer span of a <see cref="StreamManager"/>
/// subscription, or the <c>orleans.outbox.post</c> span of an outbox processor.
/// </summary>
public enum MessageTraceMode
{
    /// <summary>
    /// Link the span to the producer span with an <see cref="System.Diagnostics.ActivityLink"/>
    /// instead of parenting it. This is the default.
    /// </summary>
    Link,

    /// <summary>
    /// Start the span as a child of the producer span, in the producer's trace.
    /// </summary>
    Parent,

    /// <summary>
    /// Behave as <see cref="Parent"/> when the message's age is at most
    /// <see cref="MessageTraceOptions.MaxParentLag"/>, and as <see cref="Link"/> otherwise
    /// or when the age is unknown.
    /// </summary>
    ParentWithinLag,

    /// <summary>
    /// Join an existing trace, never start one. When an activity is ambient, the
    /// span starts as its child, with a link to the producer. When nothing is
    /// ambient, no span starts and the handler or postman runs with no activity.
    /// Metrics are still recorded.
    /// </summary>
    None,
}

/// <summary>
/// How a messaging span correlates with the producer's trace context. Used by
/// <see cref="StreamSubscriptionOptions.Trace"/> on the receive side and by the
/// outbox processor options on the send side.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Link"/> is the default. The span links back to the producer, so a
/// backlog replayed hours after an outage never stretches a producer's trace
/// across that time.
/// </para>
/// <para>
/// <see cref="Parent"/> puts the span in the producer's trace. Use it for internal
/// grain-to-grain flows with bounded fan-out, where backends that build trees from
/// the trace id, or tail samplers that decide per trace id, would otherwise split
/// one causal flow into separately sampled traces.
/// </para>
/// <para>
/// <see cref="ParentWithinLag(TimeSpan)"/> parents only when the message is recent
/// and links otherwise. A stream delivery's age is measured from the broker enqueue
/// time, so it needs a token that exposes one, such as the enriched Event Hubs
/// token. An outbox message's age is measured from when it was added to the outbox.
/// </para>
/// <para>
/// <see cref="None"/> joins an existing trace and never starts one: "do not start
/// a trace", not "never trace". A delivery that already runs inside a trace gets a
/// span that is a child of the ambient activity and links to the producer. A
/// delivery with nothing ambient, such as a stream delivery from a pulling agent or
/// an outbox drain from a timer or reminder, gets no span. Choose it to silence
/// background deliveries and redeliveries while keeping spans inside
/// request-driven work. Metrics are still recorded, and the outbox still captures
/// each message's traceparent for log correlation.
/// </para>
/// </remarks>
public sealed record MessageTraceOptions
{
    private MessageTraceOptions(MessageTraceMode mode, TimeSpan maxParentLag)
    {
        Mode = mode;
        MaxParentLag = maxParentLag;
    }

    /// <summary>
    /// Links the span to the producer span. This is the default.
    /// </summary>
    public static MessageTraceOptions Link { get; } = new(MessageTraceMode.Link, TimeSpan.Zero);

    /// <summary>
    /// Parents the span to the producer span regardless of the message's age.
    /// </summary>
    public static MessageTraceOptions Parent { get; } = new(MessageTraceMode.Parent, TimeSpan.Zero);

    /// <summary>
    /// Joins an existing trace, never starts one. See <see cref="MessageTraceMode.None"/>.
    /// </summary>
    public static MessageTraceOptions None { get; } = new(MessageTraceMode.None, TimeSpan.Zero);

    /// <summary>
    /// The correlation mode.
    /// </summary>
    public MessageTraceMode Mode { get; }

    /// <summary>
    /// The largest message age that still parents the span when
    /// <see cref="Mode"/> is <see cref="MessageTraceMode.ParentWithinLag"/>.
    /// <see cref="TimeSpan.Zero"/> for the other modes.
    /// </summary>
    public TimeSpan MaxParentLag { get; }

    /// <summary>
    /// Parents the span to the producer span when the message is at most
    /// <paramref name="maxParentLag"/> old, and links it otherwise.
    /// </summary>
    /// <param name="maxParentLag">The largest age that still parents. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxParentLag"/> is zero or negative.</exception>
    public static MessageTraceOptions ParentWithinLag(TimeSpan maxParentLag)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxParentLag, TimeSpan.Zero);
        return new(MessageTraceMode.ParentWithinLag, maxParentLag);
    }
}
