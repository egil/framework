using System.Diagnostics;
using Egil.Orleans.Messaging.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace Egil.Orleans.Messaging.Streams;

/// <summary>
/// Grain-level facade around Orleans stream handler attachment. Supports
/// Orleans implicit subscriptions and durable explicit subscription handles.
/// </summary>
public sealed class StreamManager : IStreamManagerComponent
{
    private readonly IGrainBase owner;
    private readonly Func<MessageTracker?>? getTracker;
    private readonly Func<string, IStreamProvider> getStreamProvider;
    private readonly Func<string, StreamId> getStreamId;
    private readonly Func<StreamSubscriptionOptions> createDefaultOptions;
    private readonly TimeProvider defaultTimeProvider;
    private readonly ILogger logger;
    private readonly Dictionary<string, IImplicitSubscription> implicitSubscriptions = new(StringComparer.Ordinal);
    private readonly List<IExplicitSubscription> explicitSubscriptions = [];
    private readonly List<object> subscriptionHandles = [];
    private bool configurationLocked;

    private StreamManager(
        IGrainBase owner,
        Func<MessageTracker?>? getTracker,
        Func<string, IStreamProvider> getStreamProvider,
        Func<string, StreamId> getStreamId,
        Func<StreamSubscriptionOptions> createDefaultOptions,
        TimeProvider defaultTimeProvider,
        ILogger logger)
    {
        this.owner = owner;
        this.getTracker = getTracker;
        this.getStreamProvider = getStreamProvider;
        this.getStreamId = getStreamId;
        this.createDefaultOptions = createDefaultOptions;
        this.defaultTimeProvider = defaultTimeProvider;
        this.logger = logger;
    }

    /// <summary>
    /// Creates a <see cref="StreamManager"/> for the given grain.
    /// </summary>
    internal static StreamManager Create(
        IGrainBase owner,
        Func<MessageTracker?>? getTracker)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var services = owner.GrainContext.ActivationServices;
        var loggerFactory = services.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger<StreamManager>()
            ?? NullLogger<StreamManager>.Instance;
        var optionsFactory = services.GetService<IOptionsFactory<StreamSubscriptionOptions>>();

        var manager = Create(
            owner,
            getTracker,
            services.GetRequiredKeyedService<IStreamProvider>,
            streamNamespace => CreateStreamId(streamNamespace, owner.GrainContext.GrainId),
            logger,
            optionsFactory is null ? null : () => optionsFactory.Create(Options.DefaultName),
            services.GetService<TimeProvider>());

        manager.AttachToGrain();
        return manager;
    }

    internal static StreamManager Create(
        IGrainBase owner,
        Func<MessageTracker?>? getTracker,
        Func<string, IStreamProvider> getStreamProvider,
        Func<string, StreamId> getStreamId,
        ILogger logger,
        Func<StreamSubscriptionOptions>? createDefaultOptions = null,
        TimeProvider? defaultTimeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(getStreamProvider);
        ArgumentNullException.ThrowIfNull(getStreamId);
        ArgumentNullException.ThrowIfNull(logger);

        return new StreamManager(
            owner,
            getTracker,
            getStreamProvider,
            getStreamId,
            createDefaultOptions ?? (() => new StreamSubscriptionOptions()),
            defaultTimeProvider ?? TimeProvider.System,
            logger);
    }

    /// <summary>
    /// Configures handler attachment for an Orleans implicit stream subscription.
    /// </summary>
    /// <param name="streamNamespace">The stream namespace the implicit subscription is bound to.</param>
    /// <param name="onNextAsync">Handles each delivered event with its cursor.</param>
    /// <param name="configure">
    /// Overrides the silo-wide <see cref="StreamSubscriptionOptions"/> defaults
    /// for this subscription. Omit it to use the defaults unchanged.
    /// </param>
    /// <returns>This manager, for chaining.</returns>
    public StreamManager ConfigureImplicitSubscription<TEvent>(
        string streamNamespace,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        Action<StreamSubscriptionOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);
        ArgumentNullException.ThrowIfNull(onNextAsync);
        EnsureCanConfigure();

        if (implicitSubscriptions.ContainsKey(streamNamespace))
        {
            throw new InvalidOperationException(
                $"An implicit stream subscription for namespace '{streamNamespace}' has already been configured.");
        }

        implicitSubscriptions.Add(
            streamNamespace,
            new ImplicitSubscription<TEvent>(streamNamespace, onNextAsync, ResolveSettings(configure)));

        return this;
    }

    /// <inheritdoc cref="ConfigureImplicitSubscription{TEvent}(string, Func{TEvent, StreamCursor, ValueTask}, Action{StreamSubscriptionOptions}?)"/>
    public StreamManager ConfigureImplicitSubscription<TEvent>(
        string streamNamespace,
        Func<TEvent, StreamCursor, Task> onNextAsync,
        Action<StreamSubscriptionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(onNextAsync);

        return ConfigureImplicitSubscription<TEvent>(
            streamNamespace,
            (item, cursor) => new ValueTask(onNextAsync(item, cursor)),
            configure);
    }

    /// <summary>
    /// Configures a durable Orleans explicit stream subscription using the
    /// library's grain-keyed stream id convention.
    /// </summary>
    /// <remarks>
    /// The stream id is derived from the complete receiving grain identity and
    /// <paramref name="streamNamespace"/>. Publishers targeting this
    /// subscription must use <see cref="CreateStreamId(string, GrainId)"/> with
    /// that receiving grain's identity. Use the <see cref="StreamId"/> overload
    /// for an application-owned identity which must not change with the grain
    /// type.
    /// </remarks>
    /// <param name="streamProviderName">The name of the Orleans stream provider.</param>
    /// <param name="streamNamespace">The stream namespace.</param>
    /// <param name="onNextAsync">Handles each delivered event with its cursor.</param>
    /// <param name="configure">
    /// Overrides the silo-wide <see cref="StreamSubscriptionOptions"/> defaults
    /// for this subscription. Omit it to use the defaults unchanged.
    /// </param>
    /// <returns>This manager, for chaining.</returns>
    public StreamManager ConfigureExplicitSubscription<TEvent>(
        string streamProviderName,
        string streamNamespace,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        Action<StreamSubscriptionOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);
        ArgumentNullException.ThrowIfNull(onNextAsync);
        EnsureCanConfigure();

        if (explicitSubscriptions.Any(subscription =>
            subscription.StreamProviderName == streamProviderName
            && subscription.StreamNamespace == streamNamespace))
        {
            throw new InvalidOperationException(
                $"An explicit stream subscription for provider '{streamProviderName}' and namespace '{streamNamespace}' has already been configured.");
        }

        explicitSubscriptions.Add(new ExplicitSubscription<TEvent>(
            streamProviderName,
            getStreamId(streamNamespace),
            onNextAsync,
            ResolveSettings(configure)));

        return this;
    }

    /// <inheritdoc cref="ConfigureExplicitSubscription{TEvent}(string, string, Func{TEvent, StreamCursor, ValueTask}, Action{StreamSubscriptionOptions}?)"/>
    public StreamManager ConfigureExplicitSubscription<TEvent>(
        string streamProviderName,
        string streamNamespace,
        Func<TEvent, StreamCursor, Task> onNextAsync,
        Action<StreamSubscriptionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(onNextAsync);

        return ConfigureExplicitSubscription<TEvent>(
            streamProviderName,
            streamNamespace,
            (item, cursor) => new ValueTask(onNextAsync(item, cursor)),
            configure);
    }

    /// <summary>
    /// Creates the stream identity used by grain-keyed explicit subscriptions.
    /// </summary>
    /// <remarks>
    /// Uses Orleans' textual <see cref="GrainId"/> representation so producers
    /// and receiving grains can independently derive the same durable stream
    /// identity. This identity includes the grain type and therefore changes if
    /// that type changes. Grain identities which do not round-trip through the
    /// Orleans representation are rejected instead of risking a collision; use
    /// an explicit <see cref="StreamId"/> for those identities.
    /// </remarks>
    /// <param name="streamNamespace">The Orleans stream namespace.</param>
    /// <param name="grainId">The identity of the receiving grain.</param>
    /// <returns>The stream identity derived from <paramref name="grainId"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="streamNamespace"/> is blank, <paramref name="grainId"/>
    /// is the default value, or the grain identity cannot round-trip through
    /// Orleans' textual representation.
    /// </exception>
    public static StreamId CreateStreamId(string streamNamespace, GrainId grainId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);
        if (grainId.IsDefault)
        {
            throw new ArgumentException("GrainId must not be the default value.", nameof(grainId));
        }

        var textualGrainId = grainId.ToString();
        if (!GrainId.TryParse(textualGrainId, out var parsed) || parsed != grainId)
        {
            throw new ArgumentException(
                "GrainId cannot be represented unambiguously using Orleans' textual grain identity format. "
                + "Provide an explicit StreamId instead.",
                nameof(grainId));
        }

        return StreamId.Create(streamNamespace, textualGrainId);
    }

    /// <summary>
    /// Configures a durable Orleans explicit stream subscription for the
    /// specified stream identity.
    /// </summary>
    /// <param name="streamProviderName">The name of the Orleans stream provider.</param>
    /// <param name="streamId">The stream identity. Must have a namespace.</param>
    /// <param name="onNextAsync">Handles each delivered event with its cursor.</param>
    /// <param name="configure">
    /// Overrides the silo-wide <see cref="StreamSubscriptionOptions"/> defaults
    /// for this subscription. Omit it to use the defaults unchanged.
    /// </param>
    /// <returns>This manager, for chaining.</returns>
    public StreamManager ConfigureExplicitSubscription<TEvent>(
        string streamProviderName,
        StreamId streamId,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        Action<StreamSubscriptionOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        ArgumentNullException.ThrowIfNull(onNextAsync);
        EnsureCanConfigure();

        if (streamId.GetNamespace() is null)
        {
            throw new ArgumentException("StreamId must have a namespace.", nameof(streamId));
        }

        if (explicitSubscriptions.Any(subscription =>
            subscription.StreamProviderName == streamProviderName
            && subscription.StreamId.Equals(streamId)))
        {
            throw new InvalidOperationException(
                $"An explicit stream subscription for provider '{streamProviderName}' and stream id '{streamId}' has already been configured.");
        }

        explicitSubscriptions.Add(new ExplicitSubscription<TEvent>(
            streamProviderName,
            streamId,
            onNextAsync,
            ResolveSettings(configure)));

        return this;
    }

    /// <inheritdoc cref="ConfigureExplicitSubscription{TEvent}(string, StreamId, Func{TEvent, StreamCursor, ValueTask}, Action{StreamSubscriptionOptions}?)"/>
    public StreamManager ConfigureExplicitSubscription<TEvent>(
        string streamProviderName,
        StreamId streamId,
        Func<TEvent, StreamCursor, Task> onNextAsync,
        Action<StreamSubscriptionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(onNextAsync);

        return ConfigureExplicitSubscription<TEvent>(
            streamProviderName,
            streamId,
            (item, cursor) => new ValueTask(onNextAsync(item, cursor)),
            configure);
    }

    /// <summary>
    /// Resumes existing durable explicit subscription handles without creating
    /// new subscriptions.
    /// </summary>
    public async Task ResumeExplicitSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        configurationLocked = true;
        foreach (var subscription in explicitSubscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await subscription.ResumeExistingAsync(this, cancellationToken);
        }
    }

    /// <summary>
    /// Resumes existing durable explicit handles, or creates exactly one
    /// subscription when none exists for a configured explicit stream.
    /// </summary>
    public async Task EnsureExplicitSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        configurationLocked = true;
        foreach (var subscription in explicitSubscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await subscription.EnsureAsync(this, cancellationToken);
        }
    }

    internal void AttachToGrain()
    {
        owner.GrainContext.SetComponent<IStreamManagerComponent>(this);
    }

    async Task IStreamManagerComponent.OnSubscribedAsync(IStreamSubscriptionHandleFactory handleFactory)
    {
        ArgumentNullException.ThrowIfNull(handleFactory);

        configurationLocked = true;
        var streamNamespace = handleFactory.StreamId.GetNamespace();
        if (streamNamespace is null
            || !implicitSubscriptions.TryGetValue(streamNamespace, out var subscription))
        {
            throw new InvalidOperationException(
                $"No implicit stream subscription handler is configured for provider '{handleFactory.ProviderName}' and stream id '{handleFactory.StreamId}'. " +
                $"Call ConfigureImplicitSubscription(...) for namespace '{streamNamespace ?? "<null>"}' during activation.");
        }

        await subscription.ResumeAsync(this, handleFactory);
    }

    private SubscriptionSettings ResolveSettings(Action<StreamSubscriptionOptions>? configure)
    {
        var options = createDefaultOptions();
        configure?.Invoke(options);

        if (options.Trace is null)
        {
            throw new InvalidOperationException($"{nameof(StreamSubscriptionOptions)}.{nameof(StreamSubscriptionOptions.Trace)} must not be null.");
        }

        return new SubscriptionSettings(
            options.OnError,
            options.UseTrackedResumeToken,
            options.Trace,
            options.TimeProvider ?? defaultTimeProvider);
    }

    private async Task<StreamSubscriptionHandle<TEvent>> ResumeImplicitAsync<TEvent>(
        string streamNamespace,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        SubscriptionSettings settings,
        IStreamSubscriptionHandleFactory handleFactory)
    {
        try
        {
            var observer = new StreamObserver<TEvent>(
                this,
                streamNamespace,
                handleFactory.ProviderName,
                onNextAsync,
                settings);
            var handle = await handleFactory.Create<TEvent>().ResumeAsync(
                observer,
                GetResumeToken(handleFactory.ProviderName, streamNamespace, settings.UseTrackedResumeToken));

            subscriptionHandles.Add(handle);
            MessagingTelemetry.RecordStreamSubscription(streamNamespace, "established");
            return handle;
        }
        catch (Exception ex)
        {
            MessagingTelemetry.RecordStreamSubscription(streamNamespace, "errored");
            logger.LogError(ex, "Implicit stream subscription attach failed for namespace {StreamNamespace}.", streamNamespace);
            throw;
        }
    }

    private async Task ResumeExplicitAsync<TEvent>(
        string streamProviderName,
        StreamId streamId,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        SubscriptionSettings settings,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        var streamNamespace = streamId.GetNamespace()!;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stream = GetExplicitStream<TEvent>(streamProviderName, streamId);
            var handles = await stream.GetAllSubscriptionHandles();
            if (handles.Count == 0 && createIfMissing)
            {
                var observer = new StreamObserver<TEvent>(
                    this,
                    streamNamespace,
                    streamProviderName,
                    onNextAsync,
                    settings);
                var handle = await stream.SubscribeAsync(
                    observer,
                    GetResumeToken(streamProviderName, streamNamespace, settings.UseTrackedResumeToken),
                    filterData: null);

                subscriptionHandles.Add(handle);
                MessagingTelemetry.RecordStreamSubscription(streamNamespace, "established");
                return;
            }

            foreach (var handle in handles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var observer = new StreamObserver<TEvent>(
                    this,
                    streamNamespace,
                    handle.ProviderName,
                    onNextAsync,
                    settings);
                var resumedHandle = await handle.ResumeAsync(
                    observer,
                    GetResumeToken(handle.ProviderName, streamNamespace, settings.UseTrackedResumeToken));

                subscriptionHandles.Add(resumedHandle);
                MessagingTelemetry.RecordStreamSubscription(streamNamespace, "established");
            }
        }
        catch (Exception ex)
        {
            MessagingTelemetry.RecordStreamSubscription(streamNamespace, "errored");
            logger.LogError(
                ex,
                "Explicit stream subscription resume failed for provider {StreamProviderName} and namespace {StreamNamespace}.",
                streamProviderName,
                streamNamespace);
            throw;
        }
    }

    private IAsyncStream<TEvent> GetExplicitStream<TEvent>(
        string streamProviderName,
        StreamId streamId)
    {
        var streamProvider = getStreamProvider(streamProviderName);
        return streamProvider.GetStream<TEvent>(streamId);
    }

    private StreamSequenceToken? GetResumeToken(
        string? streamProviderName,
        string streamNamespace,
        bool useTrackedResumeToken)
    {
        if (!useTrackedResumeToken)
        {
            return null;
        }

        // Resolve at subscription time: hydration or a later read can replace
        // the tracker instance captured during grain construction.
        var trackerSnapshot = getTracker?.Invoke();
        var cursor = string.IsNullOrWhiteSpace(streamProviderName)
            ? trackerSnapshot?.LatestStream(streamNamespace)
            : trackerSnapshot?.LatestStream(streamProviderName, streamNamespace);

        return cursor?.Token;
    }

    private async Task OnNextAsync<TEvent>(
        string streamNamespace,
        string? providerName,
        TEvent item,
        StreamSequenceToken? token,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        SubscriptionSettings settings)
    {
        var cursor = new StreamCursor(streamNamespace, token, providerName);
        var started = Stopwatch.GetTimestamp();

        var ambient = Activity.Current;
        var activity = StartConsumerActivity(streamNamespace, cursor, settings);
        try
        {
            await onNextAsync(item, cursor);

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
            HandleError(streamNamespace, ex, settings.OnError);
        }
        finally
        {
            activity?.Dispose();

            // A linked span starts as a root, so stopping it leaves no current
            // activity. Put back whatever was ambient before delivery.
            Activity.Current = ambient;
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

    private Activity? StartConsumerActivity(string streamNamespace, StreamCursor cursor, SubscriptionSettings settings)
    {
        // None never starts a trace, but still traces inside one that already exists.
        if (settings.Trace.Mode == MessageTraceMode.None && Activity.Current is null)
        {
            return null;
        }

        var tags = new KeyValuePair<string, object?>[]
        {
            new("messaging.system", "orleans"),
            new("messaging.operation", "process"),
            new("messaging.destination.name", streamNamespace),
            new("orleans.grain.id", owner.GrainContext.GrainId.ToString())
        };

        var hasProducer = cursor.TryGetTraceParent(out var traceParent)
            && ActivityContext.TryParse(traceParent, traceState: null, isRemote: true, out var producerContext);

        if (settings.Trace.Mode == MessageTraceMode.None)
        {
            return MessagingTelemetry.ActivitySource.StartActivity(
                "orleans.stream.process",
                ActivityKind.Consumer,
                parentContext: default,
                tags: tags,
                links: hasProducer ? [new ActivityLink(producerContext)] : null);
        }

        if (hasProducer)
        {
            if (ShouldParent(cursor, settings))
            {
                return MessagingTelemetry.ActivitySource.StartActivity(
                    "orleans.stream.process",
                    ActivityKind.Consumer,
                    parentContext: producerContext,
                    tags: tags);
            }

            // parentContext: default does not force a root span: ActivitySource falls
            // back to Activity.Current. A linked delivery must start its own trace, so
            // clear the ambient activity first; OnNextAsync restores it afterwards.
            var ambient = Activity.Current;
            Activity.Current = null;
            var linked = MessagingTelemetry.ActivitySource.StartActivity(
                "orleans.stream.process",
                ActivityKind.Consumer,
                parentContext: default,
                tags: tags,
                links: [new ActivityLink(producerContext)]);

            // Nothing sampled the span, so the handler runs under the ambient activity.
            Activity.Current ??= ambient;
            return linked;
        }

        return MessagingTelemetry.ActivitySource.StartActivity(
            "orleans.stream.process",
            ActivityKind.Consumer,
            parentContext: default,
            tags: tags);
    }

    private static bool ShouldParent(StreamCursor cursor, SubscriptionSettings settings) => settings.Trace.Mode switch
    {
        MessageTraceMode.Parent => true,
        MessageTraceMode.ParentWithinLag => cursor.TryGetEnqueuedTime(out var enqueuedTime)
            && settings.TimeProvider.GetUtcNow() - enqueuedTime <= settings.Trace.MaxParentLag,
        _ => false,
    };

    private void EnsureCanConfigure()
    {
        if (configurationLocked)
        {
            throw new InvalidOperationException("Cannot configure stream subscriptions after handler attachment or explicit resume has started.");
        }
    }

    private sealed record SubscriptionSettings(
        Action<string, Exception>? OnError,
        bool UseTrackedResumeToken,
        MessageTraceOptions Trace,
        TimeProvider TimeProvider);

    private interface IImplicitSubscription
    {
        Task ResumeAsync(StreamManager manager, IStreamSubscriptionHandleFactory handleFactory);
    }

    private interface IExplicitSubscription
    {
        string StreamProviderName { get; }

        StreamId StreamId { get; }

        string StreamNamespace { get; }

        Task ResumeExistingAsync(StreamManager manager, CancellationToken cancellationToken);

        Task EnsureAsync(StreamManager manager, CancellationToken cancellationToken);
    }

    private sealed class ImplicitSubscription<TEvent>(
        string streamNamespace,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        SubscriptionSettings settings)
        : IImplicitSubscription
    {
        public Task ResumeAsync(StreamManager manager, IStreamSubscriptionHandleFactory handleFactory) =>
            manager.ResumeImplicitAsync(streamNamespace, onNextAsync, settings, handleFactory);
    }

    private sealed class ExplicitSubscription<TEvent>(
        string streamProviderName,
        StreamId streamId,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        SubscriptionSettings settings)
        : IExplicitSubscription
    {
        public string StreamProviderName => streamProviderName;

        public StreamId StreamId => streamId;

        public string StreamNamespace => streamId.GetNamespace()!;

        public Task ResumeExistingAsync(StreamManager manager, CancellationToken cancellationToken) =>
            manager.ResumeExplicitAsync(
                streamProviderName,
                streamId,
                onNextAsync,
                settings,
                createIfMissing: false,
                cancellationToken);

        public Task EnsureAsync(StreamManager manager, CancellationToken cancellationToken) =>
            manager.ResumeExplicitAsync(
                streamProviderName,
                streamId,
                onNextAsync,
                settings,
                createIfMissing: true,
                cancellationToken);
    }

    private sealed class StreamObserver<TEvent>(
        StreamManager manager,
        string streamNamespace,
        string? providerName,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        SubscriptionSettings settings)
        : IAsyncObserver<TEvent>
    {
        public Task OnNextAsync(TEvent item, StreamSequenceToken? token = null) =>
            manager.OnNextAsync(streamNamespace, providerName, item, token, onNextAsync, settings);

        public Task OnCompletedAsync() => Task.CompletedTask;

        public Task OnErrorAsync(Exception ex) => manager.OnErrorAsync(streamNamespace, ex, settings.OnError);
    }
}
