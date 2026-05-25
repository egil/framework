using System.Diagnostics;
using Egil.Orleans.Messaging.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly MessageTracker? trackerSnapshot;
    private readonly Func<string, IStreamProvider> getStreamProvider;
    private readonly Func<string, StreamId> getStreamId;
    private readonly ILogger logger;
    private readonly Dictionary<string, IImplicitSubscription> implicitSubscriptions = new(StringComparer.Ordinal);
    private readonly List<IExplicitSubscription> explicitSubscriptions = [];
    private readonly List<object> subscriptionHandles = [];
    private bool configurationLocked;

    private StreamManager(
        IGrainBase owner,
        MessageTracker? trackerSnapshot,
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
    internal static StreamManager Create(
        IGrainBase owner,
        MessageTracker? trackerSnapshot)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var services = owner.GrainContext.ActivationServices;
        var loggerFactory = services.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger<StreamManager>()
            ?? NullLogger<StreamManager>.Instance;

        var manager = Create(
            owner,
            trackerSnapshot,
            services.GetRequiredKeyedService<IStreamProvider>,
            streamNamespace => CreateStreamId(owner, streamNamespace),
            logger);

        manager.AttachToGrain();
        return manager;
    }

    internal static StreamManager Create(
        IGrainBase owner,
        MessageTracker? trackerSnapshot,
        Func<string, IStreamProvider> getStreamProvider,
        Func<string, StreamId> getStreamId,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(getStreamProvider);
        ArgumentNullException.ThrowIfNull(getStreamId);
        ArgumentNullException.ThrowIfNull(logger);

        return new StreamManager(owner, trackerSnapshot, getStreamProvider, getStreamId, logger);
    }

    /// <summary>
    /// Configures handler attachment for an Orleans implicit stream subscription.
    /// </summary>
    public StreamManager ConfigureImplicitSubscription<TEvent>(
        string streamNamespace,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        Action<string, Exception>? onError = default,
        bool useTrackedResumeToken = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);
        ArgumentNullException.ThrowIfNull(onNextAsync);
        EnsureCanConfigure();

        if (!implicitSubscriptions.TryAdd(
            streamNamespace,
            new ImplicitSubscription<TEvent>(streamNamespace, onNextAsync, onError, useTrackedResumeToken)))
        {
            throw new InvalidOperationException(
                $"An implicit stream subscription for namespace '{streamNamespace}' has already been configured.");
        }

        return this;
    }

    /// <inheritdoc cref="ConfigureImplicitSubscription{TEvent}(string, Func{TEvent, StreamCursor, ValueTask}, Action{string, Exception}?, bool)"/>
    public StreamManager ConfigureImplicitSubscription<TEvent>(
        string streamNamespace,
        Func<TEvent, StreamCursor, Task> onNextAsync,
        Action<string, Exception>? onError = default,
        bool useTrackedResumeToken = true)
    {
        ArgumentNullException.ThrowIfNull(onNextAsync);

        return ConfigureImplicitSubscription<TEvent>(
            streamNamespace,
            (item, cursor) => new ValueTask(onNextAsync(item, cursor)),
            onError,
            useTrackedResumeToken);
    }

    /// <summary>
    /// Configures a durable Orleans explicit stream subscription using the
    /// library's grain-keyed stream id convention.
    /// </summary>
    /// <remarks>
    /// The stream id is derived from the receiving grain identity and
    /// <paramref name="streamNamespace"/>. Use the <see cref="StreamId"/>
    /// overload when the explicit stream is keyed by another application id.
    /// </remarks>
    public StreamManager ConfigureExplicitSubscription<TEvent>(
        string streamProviderName,
        string streamNamespace,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        Action<string, Exception>? onError = default,
        bool useTrackedResumeToken = true)
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
            onError,
            useTrackedResumeToken));

        return this;
    }

    /// <inheritdoc cref="ConfigureExplicitSubscription{TEvent}(string, string, Func{TEvent, StreamCursor, ValueTask}, Action{string, Exception}?, bool)"/>
    public StreamManager ConfigureExplicitSubscription<TEvent>(
        string streamProviderName,
        string streamNamespace,
        Func<TEvent, StreamCursor, Task> onNextAsync,
        Action<string, Exception>? onError = default,
        bool useTrackedResumeToken = true)
    {
        ArgumentNullException.ThrowIfNull(onNextAsync);

        return ConfigureExplicitSubscription<TEvent>(
            streamProviderName,
            streamNamespace,
            (item, cursor) => new ValueTask(onNextAsync(item, cursor)),
            onError,
            useTrackedResumeToken);
    }

    /// <summary>
    /// Configures a durable Orleans explicit stream subscription for the
    /// specified stream identity.
    /// </summary>
    public StreamManager ConfigureExplicitSubscription<TEvent>(
        string streamProviderName,
        StreamId streamId,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        Action<string, Exception>? onError = default,
        bool useTrackedResumeToken = true)
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
            onError,
            useTrackedResumeToken));

        return this;
    }

    /// <inheritdoc cref="ConfigureExplicitSubscription{TEvent}(string, StreamId, Func{TEvent, StreamCursor, ValueTask}, Action{string, Exception}?, bool)"/>
    public StreamManager ConfigureExplicitSubscription<TEvent>(
        string streamProviderName,
        StreamId streamId,
        Func<TEvent, StreamCursor, Task> onNextAsync,
        Action<string, Exception>? onError = default,
        bool useTrackedResumeToken = true)
    {
        ArgumentNullException.ThrowIfNull(onNextAsync);

        return ConfigureExplicitSubscription<TEvent>(
            streamProviderName,
            streamId,
            (item, cursor) => new ValueTask(onNextAsync(item, cursor)),
            onError,
            useTrackedResumeToken);
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

    private async Task<StreamSubscriptionHandle<TEvent>> ResumeImplicitAsync<TEvent>(
        string streamNamespace,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        Action<string, Exception>? onError,
        bool useTrackedResumeToken,
        IStreamSubscriptionHandleFactory handleFactory)
    {
        try
        {
            var observer = new StreamObserver<TEvent>(
                this,
                streamNamespace,
                handleFactory.ProviderName,
                onNextAsync,
                onError);
            var handle = await handleFactory.Create<TEvent>().ResumeAsync(
                observer,
                GetResumeToken(handleFactory.ProviderName, streamNamespace, useTrackedResumeToken));

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
        Action<string, Exception>? onError,
        bool useTrackedResumeToken,
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
                    onError);
                var handle = await stream.SubscribeAsync(
                    observer,
                    GetResumeToken(streamProviderName, streamNamespace, useTrackedResumeToken),
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
                    onError);
                var resumedHandle = await handle.ResumeAsync(
                    observer,
                    GetResumeToken(handle.ProviderName, streamNamespace, useTrackedResumeToken));

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
        Action<string, Exception>? onError)
    {
        var cursor = new StreamCursor(streamNamespace, token, providerName);
        var started = Stopwatch.GetTimestamp();

        using var activity = StartConsumerActivity(streamNamespace, cursor);
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

    private void EnsureCanConfigure()
    {
        if (configurationLocked)
        {
            throw new InvalidOperationException("Cannot configure stream subscriptions after handler attachment or explicit resume has started.");
        }
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
        Action<string, Exception>? onError,
        bool useTrackedResumeToken)
        : IImplicitSubscription
    {
        public Task ResumeAsync(StreamManager manager, IStreamSubscriptionHandleFactory handleFactory) =>
            manager.ResumeImplicitAsync(streamNamespace, onNextAsync, onError, useTrackedResumeToken, handleFactory);
    }

    private sealed class ExplicitSubscription<TEvent>(
        string streamProviderName,
        StreamId streamId,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        Action<string, Exception>? onError,
        bool useTrackedResumeToken)
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
                onError,
                useTrackedResumeToken,
                createIfMissing: false,
                cancellationToken);

        public Task EnsureAsync(StreamManager manager, CancellationToken cancellationToken) =>
            manager.ResumeExplicitAsync(
                streamProviderName,
                streamId,
                onNextAsync,
                onError,
                useTrackedResumeToken,
                createIfMissing: true,
                cancellationToken);
    }

    private sealed class StreamObserver<TEvent>(
        StreamManager manager,
        string streamNamespace,
        string? providerName,
        Func<TEvent, StreamCursor, ValueTask> onNextAsync,
        Action<string, Exception>? onError)
        : IAsyncObserver<TEvent>
    {
        public Task OnNextAsync(TEvent item, StreamSequenceToken? token = null) =>
            manager.OnNextAsync(streamNamespace, providerName, item, token, onNextAsync, onError);

        public Task OnCompletedAsync() => Task.CompletedTask;

        public Task OnErrorAsync(Exception ex) => manager.OnErrorAsync(streamNamespace, ex, onError);
    }
}