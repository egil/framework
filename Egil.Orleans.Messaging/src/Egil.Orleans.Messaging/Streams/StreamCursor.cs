using System.Text.Json.Serialization;
using Egil.Orleans.Messaging.Tracking;
using Egil.Orleans.Messaging.Outboxes;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Streams;

/// <summary>
/// Wraps a stream namespace and associated <see cref="StreamSequenceToken"/>
/// into a single value that <see cref="MessageTracker"/> uses for dedup and
/// <see cref="StreamManager"/> uses for resume.
/// </summary>
/// <remarks>
/// <para>
/// <b>Projection constraint:</b> <see cref="StreamSequenceToken"/> is an abstract
/// type. The library ships an STJ converter that handles explicitly
/// registered token converters via a <c>Kind</c> discriminator:
/// <list type="bullet">
/// <item><c>EventSequenceToken</c> — Orleans SimpleMessageStream</item>
/// <item><c>EventSequenceTokenV2</c> — Orleans SimpleMessageStream v2</item>
/// </list>
/// Provider packages can register additional concrete token converters through
/// <see cref="StreamSequenceTokenJsonConverters"/>. Unknown subtypes throw at
/// serialization time — silently dropping the cursor would corrupt dedup
/// state.
/// </para>
/// <para>
/// <b>Provider-specific metadata:</b> Tokens can implement
/// <see cref="IStreamSequenceTokenMetadata"/> to expose broker-side enqueue
/// time, provider name, or trace context without coupling this core package to
/// a specific streaming provider.
/// </para>
/// <para>
/// <b>Serialization:</b> Decorated with <c>[GenerateSerializer]</c> for Orleans
/// and <c>[JsonConverter]</c> for System.Text.Json. The STJ converter handles
/// the polymorphic <see cref="StreamSequenceToken"/> via the discriminator.
/// </para>
/// </remarks>
/// <param name="StreamNamespace">The Orleans stream namespace within the grain.</param>
/// <param name="Token">
/// The opaque sequence token for resumption. May be <c>null</c> when no prior
/// position exists (subscribe from provider default).
/// </param>
/// <param name="ProviderName">
/// Optional provider name observed at runtime. <see cref="StreamManager"/>
/// populates this from <see cref="StreamSubscriptionHandle{T}.ProviderName"/>
/// when available.
/// </param>
[GenerateSerializer]
[Alias("egil.orleans.messaging.StreamCursor")]
[JsonConverter(typeof(StreamCursorJsonConverter))]
public sealed record StreamCursor(
    [property: Id(0)] string StreamNamespace,
    [property: Id(1)] StreamSequenceToken? Token,
    [property: Id(2)] string? ProviderName = null)
{
    /// <summary>The complete source identity, absent only in legacy cursors.</summary>
    [Id(3)] public StreamId? StreamId { get; init; }

    /// <summary>The logical outbox identity, independent of the provider position.</summary>
    [Id(4)] public OutboxSequenceToken? OutboxToken { get; init; }

    /// <summary>Validates complete source metadata before tracking or persisting it.</summary>
    internal void ValidateSource()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(StreamNamespace);
        if (StreamId is { } id && !string.Equals(id.GetNamespace(), StreamNamespace, StringComparison.Ordinal))
            throw new ArgumentException("StreamId namespace must agree with StreamNamespace.");
        if (OutboxToken is { } identity)
        {
            if (StreamId is null || !TryGetProviderName(out _))
                throw new ArgumentException("Outbox stream identity requires a full StreamId and provider name.");
            OutboxStreamContext.Validate(identity);
        }
    }

    /// <summary>
    /// Attempts to extract the broker-side enqueue time from the underlying
    /// <see cref="Token"/>.
    /// </summary>
    /// <remarks>
    /// Returns <c>true</c> when the token implements
    /// <see cref="IStreamSequenceTokenMetadata"/> and exposes an enqueue time.
    /// </remarks>
    /// <param name="enqueuedTime">
    /// The time the event was enqueued at the broker, or <c>default</c> if
    /// the token type does not carry this information.
    /// </param>
    /// <returns>
    /// <c>true</c> if the time was extracted; <c>false</c> otherwise.
    /// </returns>
    public bool TryGetEnqueuedTime(out DateTimeOffset enqueuedTime)
    {
        if (Token is IStreamSequenceTokenMetadata metadata
            && metadata.TryGetEnqueuedTime(out enqueuedTime))
        {
            return true;
        }

        enqueuedTime = default;
        return false;
    }

    /// <summary>
    /// Attempts to extract the stream provider name from the underlying
    /// <see cref="Token"/>.
    /// </summary>
    /// <remarks>
    /// Returns <c>true</c> when the token implements
    /// <see cref="IStreamSequenceTokenMetadata"/> and exposes a provider name,
    /// or when <see cref="ProviderName"/> is populated.
    /// </remarks>
    /// <param name="providerName">
    /// The name of the stream provider that delivered this event, or
    /// <c>null</c> if the token type does not carry this information.
    /// </param>
    /// <returns>
    /// <c>true</c> if a provider name was extracted; <c>false</c> otherwise.
    /// </returns>
    public bool TryGetProviderName([NotNullWhen(true)] out string? providerName)
    {
        // Subscription metadata is authoritative even if a token retained an older adapter name.
        if (!string.IsNullOrWhiteSpace(ProviderName))
        {
            providerName = ProviderName;
            return true;
        }

        if (Token is IStreamSequenceTokenMetadata metadata && metadata.TryGetProviderName(out providerName))
        {
            return true;
        }

        providerName = null;
        return false;
    }

    /// <summary>
    /// Attempts to extract the W3C <c>traceparent</c> value from the underlying
    /// <see cref="Token"/>.
    /// </summary>
    /// <remarks>
    /// Returns <c>true</c> when the token implements
    /// <see cref="IStreamSequenceTokenMetadata"/> and exposes a traceparent.
    /// <see cref="StreamManager"/> uses this to link or parent the consumer
    /// span to the producer span, according to the subscription's
    /// <see cref="MessageTraceOptions"/>.
    /// </remarks>
    /// <param name="traceParent">
    /// The W3C <c>traceparent</c> value from the producer-side
    /// <see cref="System.Diagnostics.Activity"/>, or <c>null</c> if the token
    /// type does not carry this information or no activity was active at
    /// publish time.
    /// </param>
    /// <returns>
    /// <c>true</c> if a traceparent was extracted; <c>false</c> otherwise.
    /// </returns>
    public bool TryGetTraceParent([NotNullWhen(true)] out string? traceParent)
    {
        if (Token is IStreamSequenceTokenMetadata metadata
            && metadata.TryGetTraceParent(out traceParent))
        {
            return true;
        }

        traceParent = null;
        return false;
    }
}
