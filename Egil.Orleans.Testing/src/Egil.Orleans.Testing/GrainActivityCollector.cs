using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace Egil.Orleans.Testing;

/// <summary>
/// Collects grain activity signals and provides deterministic wait helpers for Orleans integration tests.
/// </summary>
/// <remarks>
/// Register an instance with the <c>AddGrainActivityCollector</c> extension method from
/// <see cref="global::Orleans.Hosting.GrainActivityCollectorSiloBuilderExtensions"/>
/// and optionally enable storage collection through <see cref="GrainActivityCollectorBuilder"/>.
/// The standard <c>WaitForAssertionAsync</c> overloads retry assertions based on observed grain activity,
/// while the advanced wait methods can observe low-level storage operations and incoming grain calls directly.
/// <see cref="GrainActivityCollector"/> also implements <see cref="IGrainActivityWaiter"/>, so fixtures can
/// forward a single low-level wait primitive and expose the same wait surface through
/// <see cref="GrainActivityWaiterExtensions"/> without forcing callers through a <c>fixture.Collector</c> hop.
/// <para>
/// The collector implements <see cref="IDisposable"/>. Calling <see cref="Dispose"/> completes all
/// active subscriber channels (causing any pending <c>ReadAllAsync</c> loops to terminate), removes
/// every subscription, and clears the recent-event history. After disposal, <c>WaitFor*</c> and
/// <c>SubscribeTo*</c> methods throw <see cref="ObjectDisposedException"/>, while internal publish
/// methods become silent no-ops to avoid cascading failures during shutdown.
/// </para>
/// </remarks>
public sealed class GrainActivityCollector : IGrainActivityWaiter, IDisposable
{
    private const int RecentEventHistoryCapacity = 256;

    private readonly Lock activitySubscribersLock = new();
    private readonly Lock storageSubscribersLock = new();
    private readonly Lock grainCallSubscribersLock = new();
    private bool disposed;

    private List<ActivitySubscriber> activitySubscribers = [];
    private List<StorageSubscriber> storageSubscribers = [];
    private List<GrainCallSubscriber> grainCallSubscribers = [];
    private List<LiveFeedSubscriber<StorageOperation>> liveFeedStorageSubscribers = [];
    private List<LiveFeedSubscriber<IIncomingGrainCallContext>> liveFeedGrainCallSubscribers = [];
    private Queue<StorageOperation> recentStorageOperations = new();
    private Queue<IIncomingGrainCallContext> recentGrainCalls = new();

    /// <inheritdoc cref="IGrainActivityWaiter.WaitForAssertionAsync{TResult}"/>
    [StackTraceHidden]
    public Task<TResult> WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => WaitForAssertionAsyncCore(assertion, filter, timeout, grainId: null, cancellationToken);

    /// <summary>
    /// Waits for a storage operation matching the supplied predicate.
    /// </summary>
    /// <param name="predicate">Returns <see langword="true"/> when the expected operation has been observed.</param>
    /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, <see cref="IGrainActivityWaiter.DefaultWaitTimeout"/> is used.</param>
    /// <param name="ct">A token that cancels the wait.</param>
    /// <returns>A task that completes when a matching storage operation is observed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="WaitForAssertionTimeoutException">Thrown when a matching operation is not observed before the timeout expires.</exception>
    /// <remarks>
    /// <para><b>Coupling risk:</b> This method inspects low-level persistence behavior rather than externally observable grain behavior.</para>
    /// <para>
    /// Tests that wait on storage operations are tightly coupled to implementation details like storage providers,
    /// write timing, and persistence strategy. Prefer <c>WaitForAssertionAsync</c> when you can express the expected behavior
    /// through the grain's public API instead.
    /// </para>
    /// </remarks>
    public Task WaitForStorageOperationAsync(
        Func<StorageOperation, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        => WaitForPredicateAsyncCore(
            predicate,
            (out ChannelReader<StorageOperation> reader, out StorageOperation[] history) => SubscribeStorageOperations(out reader, out history),
            timeout,
            grainId: null,
            ct);

    /// <summary>
    /// Waits for a storage operation matching the supplied predicate on the specified grain.
    /// </summary>
    /// <remarks>
    /// <para><b>Coupling risk:</b> This method inspects low-level persistence behavior rather than externally observable grain behavior.</para>
    /// <para>
    /// Tests that wait on storage operations are tightly coupled to implementation details like storage providers,
    /// write timing, and persistence strategy. Prefer <c>WaitForAssertionAsync</c> when you can express the expected behavior
    /// through the grain's public API instead.
    /// </para>
    /// </remarks>
    public Task WaitForStorageOperationAsync<TGrain>(
        TGrain grain,
        Func<StorageOperation, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(grain);
        return WaitForPredicateAsyncCore(
            predicate,
            (out ChannelReader<StorageOperation> reader, out StorageOperation[] history) => SubscribeStorageOperations(out reader, out history, operation => operation.GrainId == grain.GetGrainId()),
            timeout,
            grain.GetGrainId(),
            ct);
    }

    /// <summary>
    /// Waits for an incoming grain call matching the supplied predicate.
    /// </summary>
    /// <param name="predicate">Returns <see langword="true"/> when the expected incoming call has been observed.</param>
    /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, <see cref="IGrainActivityWaiter.DefaultWaitTimeout"/> is used.</param>
    /// <param name="ct">A token that cancels the wait.</param>
    /// <returns>A task that completes when a matching grain call is observed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="WaitForAssertionTimeoutException">Thrown when a matching call is not observed before the timeout expires.</exception>
    /// <remarks>
    /// <para><b>Coupling risk:</b> This method inspects low-level call flow rather than externally observable grain behavior.</para>
    /// <para>
    /// Tests that wait on incoming grain calls are tightly coupled to internal call structure and can break when implementation
    /// details change even if externally visible behavior is unchanged. Prefer <c>WaitForAssertionAsync</c> for behavior-first assertions.
    /// </para>
    /// </remarks>
    public Task WaitForGrainCallAsync(
        Func<IIncomingGrainCallContext, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        => WaitForPredicateAsyncCore(
            predicate,
            (out ChannelReader<IIncomingGrainCallContext> reader, out IIncomingGrainCallContext[] history) => SubscribeGrainCalls(out reader, out history),
            timeout,
            grainId: null,
            ct);

    /// <summary>
    /// Waits for an incoming grain call matching the supplied predicate on the specified grain.
    /// </summary>
    /// <remarks>
    /// <para><b>Coupling risk:</b> This method inspects low-level call flow rather than externally observable grain behavior.</para>
    /// <para>
    /// Tests that wait on incoming grain calls are tightly coupled to internal call structure and can break when implementation
    /// details change even if externally visible behavior is unchanged. Prefer <c>WaitForAssertionAsync</c> for behavior-first assertions.
    /// </para>
    /// </remarks>
    public Task WaitForGrainCallAsync<TGrain>(
        TGrain grain,
        Func<IIncomingGrainCallContext, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(grain);
        return WaitForPredicateAsyncCore(
            predicate,
            (out ChannelReader<IIncomingGrainCallContext> reader, out IIncomingGrainCallContext[] history) => SubscribeGrainCalls(out reader, out history, context => context.TargetId == grain.GetGrainId()),
            timeout,
            grain.GetGrainId(),
            ct);
    }

    /// <summary>
    /// Returns a live, future-only feed of all storage operations observed by the collector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The subscription begins when enumeration starts (i.e. when the first <c>MoveNextAsync</c> call is made).
    /// Only operations that occur <b>after</b> enumeration begins are delivered — no historical replay is performed.
    /// Start consuming the feed <b>before</b> triggering the behavior you want to observe.
    /// </para>
    /// <para>
    /// The feed uses an unbounded buffer so slow consumers never lose events. The feed completes
    /// when <paramref name="ct"/> is cancelled or the caller stops enumerating.
    /// </para>
    /// <para><b>Coupling risk:</b> This method exposes low-level persistence implementation details.
    /// Tests using this feed are tightly coupled to storage providers, write timing, and persistence strategy.
    /// Prefer <c>WaitForAssertionAsync</c> when you can express the expected behavior through the grain's public API.</para>
    /// </remarks>
    /// <param name="ct">A token that stops the feed and removes the subscription.</param>
    /// <returns>An async enumerable that yields storage operations as they occur.</returns>
    public async IAsyncEnumerable<StorageOperation> SubscribeToStorageOperations(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateUnbounded<StorageOperation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        var subscriber = new LiveFeedSubscriber<StorageOperation>(channel, GrainIdFilter: null);
        lock (storageSubscribersLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            liveFeedStorageSubscribers = [.. liveFeedStorageSubscribers, subscriber];
        }

        try
        {
            await foreach (var operation in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return operation;
            }
        }
        finally
        {
            lock (storageSubscribersLock)
            {
                liveFeedStorageSubscribers = [.. liveFeedStorageSubscribers.Where(s => !ReferenceEquals(s, subscriber))];
            }

            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Returns a live, future-only feed of storage operations for the specified grain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The subscription begins when enumeration starts (i.e. when the first <c>MoveNextAsync</c> call is made).
    /// Only operations that occur <b>after</b> enumeration begins are delivered — no historical replay is performed.
    /// Start consuming the feed <b>before</b> triggering the behavior you want to observe.
    /// </para>
    /// <para>
    /// The feed uses an unbounded buffer so slow consumers never lose events. The feed completes
    /// when <paramref name="ct"/> is cancelled or the caller stops enumerating.
    /// </para>
    /// <para><b>Coupling risk:</b> This method exposes low-level persistence implementation details.
    /// Tests using this feed are tightly coupled to storage providers, write timing, and persistence strategy.
    /// Prefer <c>WaitForAssertionAsync</c> when you can express the expected behavior through the grain's public API.</para>
    /// </remarks>
    /// <typeparam name="TGrain">The grain interface type.</typeparam>
    /// <param name="grain">The grain whose storage operations should be included in the feed.</param>
    /// <param name="ct">A token that stops the feed and removes the subscription.</param>
    /// <returns>An async enumerable that yields storage operations for the specified grain as they occur.</returns>
    public async IAsyncEnumerable<StorageOperation> SubscribeToStorageOperations<TGrain>(
        TGrain grain,
        [EnumeratorCancellation] CancellationToken ct = default)
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(grain);
        var grainId = grain.GetGrainId();

        var channel = Channel.CreateUnbounded<StorageOperation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        var subscriber = new LiveFeedSubscriber<StorageOperation>(channel, GrainIdFilter: grainId);
        lock (storageSubscribersLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            liveFeedStorageSubscribers = [.. liveFeedStorageSubscribers, subscriber];
        }

        try
        {
            await foreach (var operation in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return operation;
            }
        }
        finally
        {
            lock (storageSubscribersLock)
            {
                liveFeedStorageSubscribers = [.. liveFeedStorageSubscribers.Where(s => !ReferenceEquals(s, subscriber))];
            }

            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Returns a live, future-only feed of all incoming grain calls observed by the collector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The subscription begins when enumeration starts (i.e. when the first <c>MoveNextAsync</c> call is made).
    /// Only calls that occur <b>after</b> enumeration begins are delivered — no historical replay is performed.
    /// Start consuming the feed <b>before</b> triggering the behavior you want to observe.
    /// </para>
    /// <para>
    /// The feed uses an unbounded buffer so slow consumers never lose events. The feed completes
    /// when <paramref name="ct"/> is cancelled or the caller stops enumerating.
    /// </para>
    /// <para><b>Coupling risk:</b> This method exposes low-level call flow rather than externally observable grain behavior.
    /// Tests using this feed are tightly coupled to internal call structure and can break when implementation details change.
    /// Prefer <c>WaitForAssertionAsync</c> for behavior-first assertions.</para>
    /// </remarks>
    /// <param name="ct">A token that stops the feed and removes the subscription.</param>
    /// <returns>An async enumerable that yields incoming grain call contexts as they occur.</returns>
    public async IAsyncEnumerable<IIncomingGrainCallContext> SubscribeToGrainCalls(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateUnbounded<IIncomingGrainCallContext>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        var subscriber = new LiveFeedSubscriber<IIncomingGrainCallContext>(channel, GrainIdFilter: null);
        lock (grainCallSubscribersLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            liveFeedGrainCallSubscribers = [.. liveFeedGrainCallSubscribers, subscriber];
        }

        try
        {
            await foreach (var context in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return context;
            }
        }
        finally
        {
            lock (grainCallSubscribersLock)
            {
                liveFeedGrainCallSubscribers = [.. liveFeedGrainCallSubscribers.Where(s => !ReferenceEquals(s, subscriber))];
            }

            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Returns a live, future-only feed of incoming grain calls for the specified grain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The subscription begins when enumeration starts (i.e. when the first <c>MoveNextAsync</c> call is made).
    /// Only calls that occur <b>after</b> enumeration begins are delivered — no historical replay is performed.
    /// Start consuming the feed <b>before</b> triggering the behavior you want to observe.
    /// </para>
    /// <para>
    /// The feed uses an unbounded buffer so slow consumers never lose events. The feed completes
    /// when <paramref name="ct"/> is cancelled or the caller stops enumerating.
    /// </para>
    /// <para><b>Coupling risk:</b> This method exposes low-level call flow rather than externally observable grain behavior.
    /// Tests using this feed are tightly coupled to internal call structure and can break when implementation details change.
    /// Prefer <c>WaitForAssertionAsync</c> for behavior-first assertions.</para>
    /// </remarks>
    /// <typeparam name="TGrain">The grain interface type.</typeparam>
    /// <param name="grain">The grain whose incoming calls should be included in the feed.</param>
    /// <param name="ct">A token that stops the feed and removes the subscription.</param>
    /// <returns>An async enumerable that yields incoming grain call contexts for the specified grain as they occur.</returns>
    public async IAsyncEnumerable<IIncomingGrainCallContext> SubscribeToGrainCalls<TGrain>(
        TGrain grain,
        [EnumeratorCancellation] CancellationToken ct = default)
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(grain);
        var grainId = grain.GetGrainId();

        var channel = Channel.CreateUnbounded<IIncomingGrainCallContext>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        var subscriber = new LiveFeedSubscriber<IIncomingGrainCallContext>(channel, GrainIdFilter: grainId);
        lock (grainCallSubscribersLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            liveFeedGrainCallSubscribers = [.. liveFeedGrainCallSubscribers, subscriber];
        }

        try
        {
            await foreach (var context in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return context;
            }
        }
        finally
        {
            lock (grainCallSubscribersLock)
            {
                liveFeedGrainCallSubscribers = [.. liveFeedGrainCallSubscribers.Where(s => !ReferenceEquals(s, subscriber))];
            }

            channel.Writer.TryComplete();
        }
    }

    internal void OnStorageOperation(StorageOperation operation)
    {
        PublishStorageOperation(operation);
        PublishActivity(new GrainActivity(
            operation.GrainId,
            operation.Kind switch
            {
                StorageOperationKind.Clear => GrainActivityKind.StorageClear,
                StorageOperationKind.Read => GrainActivityKind.StorageRead,
                StorageOperationKind.Write => GrainActivityKind.StorageWrite,
                _ => throw new UnreachableException(),
            },
            DateTimeOffset.UtcNow));
    }

    internal void OnGrainCall(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PublishGrainCall(context);
        PublishActivity(new GrainActivity(context.TargetId, GrainActivityKind.GrainCall, DateTimeOffset.UtcNow));
    }

    [StackTraceHidden]
    private Task<TResult> WaitForAssertionAsyncCore<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        GrainId? grainId,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(assertion);

        var lastFailure = new StrongBox<Exception?>();
        var timeoutElapsed = new StrongBox<TimeSpan?>();
        var loopTask = WaitForAssertionAsyncLoop(assertion, filter, timeout, lastFailure, timeoutElapsed, ct);

        return loopTask
            .ContinueWith(
                continuation =>
                {
                    if (continuation.IsCompletedSuccessfully)
                    {
                        return continuation;
                    }

                    if (timeoutElapsed.Value is { } elapsed && lastFailure.Value is { } captured)
                    {
                        return Task.FromException<TResult>(CreateTimeoutException(captured, grainId, elapsed));
                    }

                    return continuation;
                },
                TaskContinuationOptions.ExecuteSynchronously)
            .Unwrap();
    }

    [StackTraceHidden]
    private async Task<TResult> WaitForAssertionAsyncLoop<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        StrongBox<Exception?> lastFailure,
        StrongBox<TimeSpan?> timeoutElapsed,
        CancellationToken ct)
    {
        using var subscription = SubscribeActivities(out var reader, filter);
        var stopwatch = Stopwatch.StartNew();

        using (RequestContextScope.ForAssertion())
        {
            try
            {
                return await assertion().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastFailure.Value = ex;
            }
        }

        using var timeoutCts = CreateTimeoutCancellationTokenSource(timeout, ct);
        var effectiveToken = timeoutCts?.Token ?? ct;

        try
        {
            await foreach (var _ in reader.ReadAllAsync(effectiveToken).ConfigureAwait(false))
            {
                using (RequestContextScope.ForAssertion())
                {
                    try
                    {
                        return await assertion().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        lastFailure.Value = ex;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timeoutElapsed.Value = stopwatch.Elapsed;
            throw;
        }

        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        throw new InvalidOperationException("The activity stream completed unexpectedly.");
    }

    private async Task WaitForPredicateAsyncCore<T>(
        Func<T, bool> predicate,
        SubscribeDelegate<T> subscribe,
        TimeSpan? timeout,
        GrainId? grainId,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(subscribe);

        using var subscription = subscribe(out var reader, out var recentItems);
        using var timeoutCts = CreateTimeoutCancellationTokenSource(timeout, ct);
        var effectiveToken = timeoutCts?.Token ?? ct;
        var stopwatch = Stopwatch.StartNew();

        foreach (var item in recentItems)
        {
            if (predicate(item))
            {
                return;
            }
        }

        try
        {
            await foreach (var item in reader.ReadAllAsync(effectiveToken).ConfigureAwait(false))
            {
                if (predicate(item))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw CreateTimeoutException(innerException: null, grainId, stopwatch.Elapsed);
        }

        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        throw new InvalidOperationException("The event stream completed unexpectedly.");
    }

    private IDisposable SubscribeActivities(out ChannelReader<GrainActivity> reader, Predicate<GrainActivity>? filter = null)
    {
        var channel = CreateChannel<GrainActivity>();
        var subscriber = new ActivitySubscriber(channel, filter);
        lock (activitySubscribersLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            activitySubscribers = [.. activitySubscribers, subscriber];
        }

        reader = channel.Reader;
        return new ActivitySubscription(this, subscriber);
    }

    private IDisposable SubscribeStorageOperations(out ChannelReader<StorageOperation> reader, out StorageOperation[] history, Predicate<StorageOperation>? filter = null)
    {
        var channel = CreateChannel<StorageOperation>();
        var subscriber = new StorageSubscriber(channel, filter);
        lock (storageSubscribersLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            history = GetHistorySnapshot(recentStorageOperations, filter);
            storageSubscribers = [.. storageSubscribers, subscriber];
        }

        reader = channel.Reader;
        return new StorageSubscription(this, subscriber);
    }

    private IDisposable SubscribeGrainCalls(out ChannelReader<IIncomingGrainCallContext> reader, out IIncomingGrainCallContext[] history, Predicate<IIncomingGrainCallContext>? filter = null)
    {
        var channel = CreateChannel<IIncomingGrainCallContext>();
        var subscriber = new GrainCallSubscriber(channel, filter);
        lock (grainCallSubscribersLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            history = GetHistorySnapshot(recentGrainCalls, filter);
            grainCallSubscribers = [.. grainCallSubscribers, subscriber];
        }

        reader = channel.Reader;
        return new GrainCallSubscription(this, subscriber);
    }

    private void PublishActivity(GrainActivity activity)
    {
        if (disposed)
        {
            return;
        }

        var snapshot = activitySubscribers;
        foreach (var subscriber in snapshot)
        {
            if (subscriber.Filter is not null && !subscriber.Filter(activity))
            {
                continue;
            }

            subscriber.Channel.Writer.TryWrite(activity);
        }
    }

    private void PublishStorageOperation(StorageOperation operation)
    {
        if (disposed)
        {
            return;
        }

        StorageSubscriber[] snapshot;
        lock (storageSubscribersLock)
        {
            EnqueueRecentEvent(recentStorageOperations, operation);
            snapshot = [.. storageSubscribers];
        }

        foreach (var subscriber in snapshot)
        {
            if (subscriber.Filter is not null && !subscriber.Filter(operation))
            {
                continue;
            }

            subscriber.Channel.Writer.TryWrite(operation);
        }

        // Live-feed subscribers use copy-on-write; reading the reference is safe without a lock.
        var liveFeedSnapshot = liveFeedStorageSubscribers;
        foreach (var subscriber in liveFeedSnapshot)
        {
            if (subscriber.GrainIdFilter is { } grainId && operation.GrainId != grainId)
            {
                continue;
            }

            subscriber.Channel.Writer.TryWrite(operation);
        }
    }

    private void PublishGrainCall(IIncomingGrainCallContext context)
    {
        if (disposed)
        {
            return;
        }

        GrainCallSubscriber[] snapshot;
        lock (grainCallSubscribersLock)
        {
            EnqueueRecentEvent(recentGrainCalls, context);
            snapshot = [.. grainCallSubscribers];
        }

        foreach (var subscriber in snapshot)
        {
            if (subscriber.Filter is not null && !subscriber.Filter(context))
            {
                continue;
            }

            subscriber.Channel.Writer.TryWrite(context);
        }

        // Live-feed subscribers use copy-on-write; reading the reference is safe without a lock.
        var liveFeedSnapshot = liveFeedGrainCallSubscribers;
        foreach (var subscriber in liveFeedSnapshot)
        {
            if (subscriber.GrainIdFilter is { } grainId && context.TargetId != grainId)
            {
                continue;
            }

            subscriber.Channel.Writer.TryWrite(context);
        }
    }

    private void Unsubscribe(ActivitySubscriber subscriber)
    {
        lock (activitySubscribersLock)
        {
            activitySubscribers = [.. activitySubscribers.Where(current => !ReferenceEquals(current, subscriber))];
        }

        subscriber.Channel.Writer.TryComplete();
    }

    private void Unsubscribe(StorageSubscriber subscriber)
    {
        lock (storageSubscribersLock)
        {
            storageSubscribers = [.. storageSubscribers.Where(current => !ReferenceEquals(current, subscriber))];
        }

        subscriber.Channel.Writer.TryComplete();
    }

    private void Unsubscribe(GrainCallSubscriber subscriber)
    {
        lock (grainCallSubscribersLock)
        {
            grainCallSubscribers = [.. grainCallSubscribers.Where(current => !ReferenceEquals(current, subscriber))];
        }

        subscriber.Channel.Writer.TryComplete();
    }

    /// <summary>
    /// Completes all active subscriber channels, removes every subscription, and clears
    /// the recent-event history. After disposal, <c>WaitFor*</c> and <c>SubscribeTo*</c>
    /// methods throw <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        List<ActivitySubscriber> activitySnapshot;
        lock (activitySubscribersLock)
        {
            activitySnapshot = activitySubscribers;
            activitySubscribers = [];
        }

        foreach (var subscriber in activitySnapshot)
        {
            subscriber.Channel.Writer.TryComplete();
        }

        List<StorageSubscriber> storageSnapshot;
        List<LiveFeedSubscriber<StorageOperation>> liveFeedStorageSnapshot;
        lock (storageSubscribersLock)
        {
            storageSnapshot = storageSubscribers;
            liveFeedStorageSnapshot = liveFeedStorageSubscribers;
            storageSubscribers = [];
            liveFeedStorageSubscribers = [];
            recentStorageOperations.Clear();
        }

        foreach (var subscriber in storageSnapshot)
        {
            subscriber.Channel.Writer.TryComplete();
        }

        foreach (var subscriber in liveFeedStorageSnapshot)
        {
            subscriber.Channel.Writer.TryComplete();
        }

        List<GrainCallSubscriber> grainCallSnapshot;
        List<LiveFeedSubscriber<IIncomingGrainCallContext>> liveFeedGrainCallSnapshot;
        lock (grainCallSubscribersLock)
        {
            grainCallSnapshot = grainCallSubscribers;
            liveFeedGrainCallSnapshot = liveFeedGrainCallSubscribers;
            grainCallSubscribers = [];
            liveFeedGrainCallSubscribers = [];
            recentGrainCalls.Clear();
        }

        foreach (var subscriber in grainCallSnapshot)
        {
            subscriber.Channel.Writer.TryComplete();
        }

        foreach (var subscriber in liveFeedGrainCallSnapshot)
        {
            subscriber.Channel.Writer.TryComplete();
        }
    }

    private static CancellationTokenSource? CreateTimeoutCancellationTokenSource(TimeSpan? timeout, CancellationToken ct)
    {
        if (Debugger.IsAttached)
        {
            return null;
        }

        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? IGrainActivityWaiter.DefaultWaitTimeout);
        return timeoutCts;
    }

    private static WaitForAssertionTimeoutException CreateTimeoutException(Exception? innerException, GrainId? grainId, TimeSpan elapsed)
    {
        var message = grainId is null
            ? $"Timed out waiting for Orleans test activity after {elapsed}."
            : $"Timed out waiting for Orleans test activity for grain '{grainId}' after {elapsed}.";

        var exception = new WaitForAssertionTimeoutException(message, innerException, grainId, elapsed);

        if (!string.IsNullOrWhiteSpace(innerException?.StackTrace))
        {
            ExceptionDispatchInfo.SetRemoteStackTrace(exception, innerException.StackTrace);
        }

        return exception;
    }

    private static Channel<T> CreateChannel<T>() =>
        Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    private static T[] GetHistorySnapshot<T>(Queue<T> history, Predicate<T>? filter)
        => filter is null
            ? [.. history]
            : [.. history.Where(item => filter(item))];

    private static void EnqueueRecentEvent<T>(Queue<T> history, T item)
    {
        history.Enqueue(item);
        while (history.Count > RecentEventHistoryCapacity)
        {
            history.Dequeue();
        }
    }

    private delegate IDisposable SubscribeDelegate<T>(out ChannelReader<T> reader, out T[] history);

    private sealed class ActivitySubscriber(Channel<GrainActivity> channel, Predicate<GrainActivity>? filter)
    {
        public Channel<GrainActivity> Channel { get; } = channel;

        public Predicate<GrainActivity>? Filter { get; } = filter;
    }

    private sealed class StorageSubscriber(Channel<StorageOperation> channel, Predicate<StorageOperation>? filter)
    {
        public Channel<StorageOperation> Channel { get; } = channel;

        public Predicate<StorageOperation>? Filter { get; } = filter;
    }

    private sealed class GrainCallSubscriber(Channel<IIncomingGrainCallContext> channel, Predicate<IIncomingGrainCallContext>? filter)
    {
        public Channel<IIncomingGrainCallContext> Channel { get; } = channel;

        public Predicate<IIncomingGrainCallContext>? Filter { get; } = filter;
    }

    private sealed class ActivitySubscription(GrainActivityCollector owner, ActivitySubscriber subscriber) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Unsubscribe(subscriber);
            }
        }
    }

    private sealed class StorageSubscription(GrainActivityCollector owner, StorageSubscriber subscriber) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Unsubscribe(subscriber);
            }
        }
    }

    private sealed class GrainCallSubscription(GrainActivityCollector owner, GrainCallSubscriber subscriber) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Unsubscribe(subscriber);
            }
        }
    }

    private sealed record LiveFeedSubscriber<T>(Channel<T> Channel, GrainId? GrainIdFilter);
}
