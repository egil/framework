using Orleans.Streams;

namespace Egil.Orleans.Messaging;

/// <summary>
/// Grain-level facade around Orleans implicit subscriptions. Manages subscribe,
/// resume, dispatch, and per-subscription error handling.
/// </summary>
/// <remarks>
/// <para>
/// <b>Composition over inheritance:</b> The grain inherits from <c>Grain</c>
/// and uses <see cref="StreamManager"/> as a field — no base-class coupling.
/// </para>
/// <para>
/// <b>Typical wiring:</b>
/// <code>
/// public override async Task OnActivateAsync(CancellationToken ct)
/// {
///     streamManager = this.InitializeStreamManager(state.Tracker)
///         .Subscribe("electricity-prices", HandlePriceTickAsync, LogStreamError)
///         .Subscribe("tariff-events", HandleTariffChangedAsync);
/// }
/// </code>
/// </para>
/// <para>
/// <b>Resume semantics:</b> each subscription reads
/// <c>trackerSnapshot.LatestStream(streamId)</c> once per subscription.
/// If a cursor exists, the <see cref="StreamSequenceToken"/> is passed to
/// <c>SubscribeAsync</c>. If <c>null</c>, subscribes without a token
/// (provider default, typically: start from current). The tracker is read
/// once at subscription time, never again — the handler is the only path that
/// mutates the tracker.
/// </para>
/// <para>
/// <b>OTel trace correlation:</b> The <c>OnNext</c> wrapper reads
/// <see cref="EnrichedEventHubSequenceToken.TraceParent"/> when the stream
/// token is enriched, parses it into an <c>ActivityContext</c>, and starts
/// the handler span with <c>ActivityKind.Consumer</c> and an
/// <c>ActivityLink</c> back to the producer. This produces separate traces
/// per delivery without collapsing weeks of traffic into one distributed trace.
/// </para>
/// <para>
/// <b>Telemetry:</b> Emits counters for messages delivered per
/// <c>(streamNamespace, accepted|rejected)</c>, subscriptions
/// established/torn-down/errored, histograms for handler latency, and
/// (when <see cref="EnrichedEventHubSequenceToken"/> is available)
/// end-to-end lag.
/// </para>
/// </remarks>
public sealed class StreamManager
{
    private StreamManager() { }

    /// <summary>
    /// Creates a <see cref="StreamManager"/> for the given grain.
    /// </summary>
    /// <param name="owner">The grain that owns this stream manager.</param>
    /// <param name="trackerSnapshot">
    /// A snapshot of the grain's <see cref="MessageTracker"/> at activation
    /// time. Used to look up resume tokens. Read once per subscription — not
    /// held afterward.
    /// </param>
    /// <returns>A <see cref="StreamManager"/> for fluent subscription configuration.</returns>
    internal static StreamManager Create(
        IGrainBase owner,
        MessageTracker trackerSnapshot)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Declares a subscription to the given <paramref name="streamNamespace"/>.
    /// </summary>
    /// <typeparam name="TEvent">
    /// The expected event type on this stream namespace. Must match the type
    /// published by the producer. Usually inferred from
    /// <paramref name="onNextAsync"/>; specify explicitly only for inline
    /// lambdas or ambiguous method groups.
    /// </typeparam>
    /// <param name="streamNamespace">
    /// The Orleans stream namespace to subscribe to. Must follow the
    /// one-provider-per-namespace convention (code-review enforced, not
    /// runtime enforced).
    /// </param>
    /// <param name="onNextAsync">
    /// Receives the deserialized event and the <see cref="StreamSequenceToken"/>
    /// representing this event's position. The handler should update the grain's
    /// <see cref="MessageTracker"/> and persist state before returning.
    /// </param>
    /// <param name="onError">
    /// Optional synchronous error handler. Receives the stream namespace and the
    /// exception. If omitted, the default behavior is to log and emit a counter
    /// without rethrowing.
    /// </param>
    /// <param name="passLatestSequenceTokenOnResume">
    /// When <c>true</c> (default), the subscription looks up the last accepted
    /// <see cref="StreamCursor"/> from the <see cref="MessageTracker"/> snapshot
    /// and passes its token to <c>SubscribeAsync</c> for resumption. When
    /// <c>false</c>, subscribes from the provider's default position (typically:
    /// current/latest).
    /// </param>
    /// <returns>
    /// This <see cref="StreamManager"/> instance for fluent subscription chaining.
    /// </returns>
    public StreamManager Subscribe<TEvent>(
        string streamNamespace,
        Func<TEvent, StreamSequenceToken, ValueTask> onNextAsync,
        Action<string, Exception>? onError = default,
        bool passLatestSequenceTokenOnResume = true)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Declares a subscription to the given <paramref name="streamNamespace"/>
    /// using a <see cref="Task"/>-returning event handler.
    /// </summary>
    /// <remarks>
    /// Convenience overload for handlers that already return <see cref="Task"/>.
    /// Implementations should adapt this to the <see cref="ValueTask"/> overload.
    /// </remarks>
    /// <typeparam name="TEvent">
    /// The expected event type on this stream namespace. Must match the type
    /// published by the producer. Usually inferred from
    /// <paramref name="onNextAsync"/>; specify explicitly only for inline
    /// lambdas or ambiguous method groups.
    /// </typeparam>
    /// <param name="streamNamespace">
    /// The Orleans stream namespace to subscribe to. Must follow the
    /// one-provider-per-namespace convention (code-review enforced, not
    /// runtime enforced).
    /// </param>
    /// <param name="onNextAsync">
    /// Receives the deserialized event and the <see cref="StreamSequenceToken"/>
    /// representing this event's position. The handler should update the grain's
    /// <see cref="MessageTracker"/> and persist state before returning.
    /// </param>
    /// <param name="onError">
    /// Optional synchronous error handler. Receives the stream namespace and the
    /// exception. If omitted, the default behavior is to log and emit a counter
    /// without rethrowing.
    /// </param>
    /// <param name="passLatestSequenceTokenOnResume">
    /// When <c>true</c> (default), the subscription looks up the last accepted
    /// <see cref="StreamCursor"/> from the <see cref="MessageTracker"/> snapshot
    /// and passes its token to <c>SubscribeAsync</c> for resumption. When
    /// <c>false</c>, subscribes from the provider's default position (typically:
    /// current/latest).
    /// </param>
    /// <returns>
    /// This <see cref="StreamManager"/> instance for fluent subscription chaining.
    /// </returns>
    public StreamManager Subscribe<TEvent>(
        string streamNamespace,
        Func<TEvent, StreamSequenceToken, Task> onNextAsync,
        Action<string, Exception>? onError = default,
        bool passLatestSequenceTokenOnResume = true)
    {
        throw new NotImplementedException();
    }
}
