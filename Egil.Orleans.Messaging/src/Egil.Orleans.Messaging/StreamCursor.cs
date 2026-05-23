using Orleans.Streams;

namespace Egil.Orleans.Messaging;

/// <summary>
/// Wraps a <see cref="StreamId"/> and its associated <see cref="StreamSequenceToken"/>
/// into a single value that <see cref="MessageTracker"/> uses for dedup and
/// <see cref="StreamManager"/> uses for resume.
/// </summary>
/// <remarks>
/// <para>
/// <b>Projection constraint:</b> <see cref="StreamSequenceToken"/> is an abstract
/// type. The library ships an STJ converter that handles a closed set of known
/// subtypes via a <c>$kind</c> discriminator:
/// <list type="bullet">
/// <item><c>EventSequenceToken</c> — Orleans SimpleMessageStream</item>
/// <item><c>EventHubSequenceToken</c> — Orleans Event Hub provider v1</item>
/// <item><c>EventHubSequenceTokenV2</c> — Orleans Event Hub provider v2</item>
/// <item><see cref="EnrichedEventHubSequenceToken"/> — library-shipped v2
/// subclass carrying <see cref="EnrichedEventHubSequenceToken.EnqueuedTime"/></item>
/// </list>
/// Unknown subtypes throw at serialization time — silently dropping the cursor
/// would corrupt dedup state.
/// </para>
/// <para>
/// <b>Enriched time:</b> Use <see cref="TryGetEnqueuedTime"/> to extract the
/// broker-side enqueue time when the token is an <see cref="EnrichedEventHubSequenceToken"/>.
/// Returns <c>false</c> for all other token types. This enables end-to-end lag
/// histograms in the <see cref="StreamManager"/> telemetry without coupling the
/// cursor type to a specific streaming provider.
/// </para>
/// <para>
/// <b>Serialization:</b> Decorated with <c>[GenerateSerializer]</c> for Orleans
/// and <c>[JsonConverter]</c> for System.Text.Json. The STJ converter handles
/// the polymorphic <see cref="StreamSequenceToken"/> via the discriminator.
/// </para>
/// </remarks>
/// <param name="StreamId">The Orleans stream identity.</param>
/// <param name="Token">
/// The opaque sequence token for resumption. May be <c>null</c> when no prior
/// position exists (subscribe from provider default).
/// </param>
[GenerateSerializer]
[Alias("egil.orleans.messaging.StreamCursor")]
// [JsonConverter(typeof(StreamCursorJsonConverter))]
public sealed record StreamCursor(
    [property: Id(0)] StreamId StreamId,
    [property: Id(1)] StreamSequenceToken? Token)
{
    /// <summary>
    /// Attempts to extract the broker-side enqueue time from the underlying
    /// <see cref="Token"/>.
    /// </summary>
    /// <remarks>
    /// Returns <c>true</c> only when the token is an
    /// <see cref="EnrichedEventHubSequenceToken"/>. Callers can use this to
    /// compute end-to-end lag (<c>now - enqueuedTime</c>) without coupling
    /// to a specific streaming provider.
    /// </remarks>
    /// <param name="enqueuedTime">
    /// The time the event was enqueued at the broker, or <c>default</c> if
    /// the token type does not carry this information.
    /// </param>
    /// <returns>
    /// <c>true</c> if <see cref="Token"/> is an
    /// <see cref="EnrichedEventHubSequenceToken"/> and the time was extracted;
    /// <c>false</c> otherwise.
    /// </returns>
    public bool TryGetEnqueuedTime(out DateTimeOffset enqueuedTime)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Attempts to extract the stream provider name from the underlying
    /// <see cref="Token"/>.
    /// </summary>
    /// <remarks>
    /// Returns <c>true</c> only when the token is an
    /// <see cref="EnrichedEventHubSequenceToken"/>. Enables provider-aware
    /// dedup and diagnostics without coupling to a specific streaming provider.
    /// </remarks>
    /// <param name="streamProviderName">
    /// The name of the stream provider that delivered this event, or
    /// <c>null</c> if the token type does not carry this information.
    /// </param>
    /// <returns>
    /// <c>true</c> if <see cref="Token"/> is an
    /// <see cref="EnrichedEventHubSequenceToken"/> and the name was extracted;
    /// <c>false</c> otherwise.
    /// </returns>
    public bool TryGetStreamProviderName([NotNullWhen(true)] out string? streamProviderName)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Attempts to extract the W3C <c>traceparent</c> value from the underlying
    /// <see cref="Token"/>.
    /// </summary>
    /// <remarks>
    /// Returns <c>true</c> only when the token is an
    /// <see cref="EnrichedEventHubSequenceToken"/> whose
    /// <see cref="EnrichedEventHubSequenceToken.TraceParent"/> is not <c>null</c>.
    /// <see cref="StreamManager"/> uses this to create
    /// <see cref="System.Diagnostics.ActivityLink"/>s — correlating consumer
    /// spans to producer spans without creating multi-hour parent-child traces.
    /// </remarks>
    /// <param name="traceParent">
    /// The W3C <c>traceparent</c> value from the producer-side
    /// <see cref="System.Diagnostics.Activity"/>, or <c>null</c> if the token
    /// type does not carry this information or no activity was active at
    /// publish time.
    /// </param>
    /// <returns>
    /// <c>true</c> if <see cref="Token"/> is an
    /// <see cref="EnrichedEventHubSequenceToken"/> with a non-null
    /// <see cref="EnrichedEventHubSequenceToken.TraceParent"/>; <c>false</c>
    /// otherwise.
    /// </returns>
    public bool TryGetTraceParent([NotNullWhen(true)] out string? traceParent)
    {
        throw new NotImplementedException();
    }
}
