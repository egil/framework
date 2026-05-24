using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
///     streamManager = this.RegisterStreamManager(state.Tracker)
///         .AddSubscription("electricity-prices", HandlePriceTickAsync, LogStreamError)
///         .AddSubscription("tariff-events", HandleTariffChangedAsync);
///     await streamManager.SubscribeAsync(ct);
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
    private readonly IGrainBase owner;
    private readonly MessageTracker trackerSnapshot;
    private readonly Func<string, IStreamProvider> getStreamProvider;
    private readonly Func<string, StreamId> getStreamId;
    private readonly ILogger logger;
    private readonly List<Func<CancellationToken, Task<object>>> subscriptions = [];
    private readonly List<object> subscriptionHandles = [];
    private bool subscribed;

    private StreamManager(
        IGrainBase owner,
        MessageTracker trackerSnapshot,
        Func<string, IStreamProvider> getStreamProvider,
        Func<string, StreamId> getStreamId,
        ILogger logger)
    {
        this.owner = owner;
        this.trackerSnapshot = trackerSnapshot;
        this.getStreamProvider = getStreamProvider;
        this.getStreamId = getStreamId;
        this.logger = logger;
    }

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
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(trackerSnapshot);

        var services = owner.GrainContext.ActivationServices;
        var loggerFactory = services.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger<StreamManager>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<StreamManager>.Instance;

        return Create(
            owner,
            trackerSnapshot,
            streamNamespace => services.GetRequiredKeyedService<IStreamProvider>(streamNamespace),
            streamNamespace => CreateStreamId(owner, streamNamespace),
            logger);
    }

    internal static StreamManager Create(
        IGrainBase owner,
        MessageTracker trackerSnapshot,
        Func<string, IStreamProvider> getStreamProvider,
        Func<string, StreamId> getStreamId,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(trackerSnapshot);
        ArgumentNullException.ThrowIfNull(getStreamProvider);
        ArgumentNullException.ThrowIfNull(getStreamId);
        ArgumentNullException.ThrowIfNull(logger);

        return new StreamManager(owner, trackerSnapshot, getStreamProvider, getStreamId, logger);
    }

    /// <summary>
    /// Adds a subscription configuration for the given <paramref name="streamNamespace"/>.
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
    /// Receives the deserialized event and optional <see cref="StreamSequenceToken"/>
    /// representing this event's position. Some stream providers may deliver a
    /// <c>null</c> token. The handler should update the grain's
    /// <see cref="MessageTracker"/> when a token is available and persist state
    /// before returning.
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
    public StreamManager AddSubscription<TEvent>(
        string streamNamespace,
        Func<TEvent, StreamSequenceToken?, ValueTask> onNextAsync,
        Action<string, Exception>? onError = default,
        bool passLatestSequenceTokenOnResume = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);
        ArgumentNullException.ThrowIfNull(onNextAsync);
        if (subscribed)
        {
            throw new InvalidOperationException("Cannot add stream subscriptions after SubscribeAsync has been called.");
        }

        var streamProvider = getStreamProvider(streamNamespace);
        var streamId = getStreamId(streamNamespace);
        var stream = streamProvider.GetStream<TEvent>(streamId);
        var observer = new StreamObserver<TEvent>(this, streamNamespace, streamId, onNextAsync, onError);
        var latestCursor = passLatestSequenceTokenOnResume
            ? trackerSnapshot.LatestStream(streamId)
            : null;

        subscriptions.Add(ct => SubscribeCoreAsync(streamNamespace, stream, observer, latestCursor?.Token, ct));
        return this;
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
    /// Receives the deserialized event and optional <see cref="StreamSequenceToken"/>
    /// representing this event's position. Some stream providers may deliver a
    /// <c>null</c> token. The handler should update the grain's
    /// <see cref="MessageTracker"/> when a token is available and persist state
    /// before returning.
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
    public StreamManager AddSubscription<TEvent>(
        string streamNamespace,
        Func<TEvent, StreamSequenceToken?, Task> onNextAsync,
        Action<string, Exception>? onError = default,
        bool passLatestSequenceTokenOnResume = true)
    {
        ArgumentNullException.ThrowIfNull(onNextAsync);

        return AddSubscription<TEvent>(
            streamNamespace,
            (item, token) => new ValueTask(onNextAsync(item, token)),
            onError,
            passLatestSequenceTokenOnResume);
    }

    /// <summary>
    /// Establishes all configured Orleans stream subscriptions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for activation-time subscription setup.</param>
    public async Task SubscribeAsync(CancellationToken cancellationToken = default)
    {
        if (subscribed)
        {
            throw new InvalidOperationException("StreamManager subscriptions have already been established.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        subscribed = true;
        var subscriptionTasks = subscriptions.Select(subscription => subscription(cancellationToken)).ToArray();

        try
        {
            var handles = await Task.WhenAll(subscriptionTasks).ConfigureAwait(false);
            subscriptionHandles.AddRange(handles);
        }
        catch
        {
            foreach (var task in subscriptionTasks)
            {
                if (task.Status is TaskStatus.RanToCompletion)
                {
                    subscriptionHandles.Add(task.Result);
                }
            }

            throw;
        }
    }

    private async Task<object> SubscribeCoreAsync<TEvent>(
        string streamNamespace,
        IAsyncStream<TEvent> stream,
        IAsyncObserver<TEvent> observer,
        StreamSequenceToken? token,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (token is null)
            {
                var handle = await stream.SubscribeAsync(observer).ConfigureAwait(false);
                MessagingTelemetry.RecordStreamSubscription(streamNamespace, "established");
                return handle;
            }
            else
            {
                var handle = await stream.SubscribeAsync(observer, token, null!).ConfigureAwait(false);
                MessagingTelemetry.RecordStreamSubscription(streamNamespace, "established");
                return handle;
            }
        }
        catch (Exception ex)
        {
            MessagingTelemetry.RecordStreamSubscription(streamNamespace, "errored");
            logger.LogError(ex, "Stream subscription failed for namespace {StreamNamespace}.", streamNamespace);
            throw;
        }
    }

    private async Task OnNextAsync<TEvent>(
        string streamNamespace,
        StreamId streamId,
        TEvent item,
        StreamSequenceToken? token,
        Func<TEvent, StreamSequenceToken?, ValueTask> onNextAsync,
        Action<string, Exception>? onError)
    {
        var cursor = new StreamCursor(streamId, token);
        var started = Stopwatch.GetTimestamp();

        using var activity = StartConsumerActivity(streamNamespace, cursor);
        try
        {
            await onNextAsync(item, token).ConfigureAwait(false);

            MessagingTelemetry.RecordStreamMessage(streamNamespace, "accepted");
            MessagingTelemetry.RecordStreamHandlerDuration(streamNamespace, "accepted", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddTag("exception.type", ex.GetType().FullName);
            activity?.AddTag("exception.message", ex.Message);
            MessagingTelemetry.RecordStreamMessage(streamNamespace, "rejected");
            MessagingTelemetry.RecordStreamHandlerError(streamNamespace);
            MessagingTelemetry.RecordStreamHandlerDuration(streamNamespace, "rejected", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            HandleError(streamNamespace, ex, onError);
        }
    }

    private Task OnErrorAsync(string streamNamespace, Exception ex, Action<string, Exception>? onError)
    {
        MessagingTelemetry.RecordStreamSubscription(streamNamespace, "errored");
        HandleError(streamNamespace, ex, onError);
        return Task.CompletedTask;
    }

    private void HandleError(string streamNamespace, Exception ex, Action<string, Exception>? onError)
    {
        if (onError is null)
        {
            logger.LogError(ex, "Stream handler failed for namespace {StreamNamespace}.", streamNamespace);
            return;
        }

        try
        {
            onError(streamNamespace, ex);
        }
        catch (Exception callbackException)
        {
            logger.LogError(
                callbackException,
                "Stream error callback failed for namespace {StreamNamespace}. Original error: {OriginalError}",
                streamNamespace,
                ex.Message);
        }
    }

    private Activity? StartConsumerActivity(string streamNamespace, StreamCursor cursor)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("messaging.system", "orleans"),
            new("messaging.operation", "process"),
            new("messaging.destination.name", streamNamespace),
            new("orleans.grain.id", owner.GrainContext.GrainId.ToString())
        };

        if (cursor.TryGetTraceParent(out var traceParent)
            && ActivityContext.TryParse(traceParent, traceState: null, isRemote: true, out var linkedContext))
        {
            return MessagingTelemetry.ActivitySource.StartActivity(
                "orleans.stream.process",
                ActivityKind.Consumer,
                parentContext: default,
                tags: tags,
                links: [new ActivityLink(linkedContext)]);
        }

        return MessagingTelemetry.ActivitySource.StartActivity(
            "orleans.stream.process",
            ActivityKind.Consumer,
            parentContext: default,
            tags: tags);
    }

    private static StreamId CreateStreamId(IGrainBase owner, string streamNamespace)
    {
        var grainId = owner.GrainContext.GrainId;
        if (GrainIdKeyExtensions.TryGetGuidKey(grainId, out var guidKey, out _))
        {
            return StreamId.Create(streamNamespace, guidKey);
        }

        if (GrainIdKeyExtensions.TryGetIntegerKey(grainId, out var longKey, out _))
        {
            return StreamId.Create(streamNamespace, longKey);
        }

        return StreamId.Create(streamNamespace, grainId.Key.ToString());
    }

    private sealed class StreamObserver<TEvent>(
        StreamManager manager,
        string streamNamespace,
        StreamId streamId,
        Func<TEvent, StreamSequenceToken?, ValueTask> onNextAsync,
        Action<string, Exception>? onError)
        : IAsyncObserver<TEvent>
    {
        public Task OnNextAsync(TEvent item, StreamSequenceToken? token = null) =>
            manager.OnNextAsync(streamNamespace, streamId, item, token, onNextAsync, onError);

        public Task OnCompletedAsync() => Task.CompletedTask;

        public Task OnErrorAsync(Exception ex) => manager.OnErrorAsync(streamNamespace, ex, onError);
    }
}
