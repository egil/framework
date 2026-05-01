using System.Diagnostics;
using System.Threading.Channels;
using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.Testing;

/// <summary>
/// Collects grain activity signals and provides deterministic wait helpers for Orleans integration tests.
/// </summary>
/// <remarks>
/// Register an instance with the <c>AddGrainActivityCollector</c> extension method from
/// <see cref="GrainActivityCollectorSiloBuilderExtensions"/>
/// and optionally enable storage collection through <see cref="GrainActivityCollectorBuilder"/>.
/// The standard <c>WaitForAssertionAsync</c> overloads retry assertions based on observed grain activity,
/// while the advanced wait methods can observe low-level storage operations and incoming grain calls directly.
/// </remarks>
public sealed class GrainActivityCollector
{
    private const int SubscriberChannelCapacity = 256;

    private readonly object activitySubscribersLock = new();
    private readonly object storageSubscribersLock = new();
    private readonly object grainCallSubscribersLock = new();

    private List<ActivitySubscriber> activitySubscribers = [];
    private List<StorageSubscriber> storageSubscribers = [];
    private List<GrainCallSubscriber> grainCallSubscribers = [];

    /// <summary>
    /// Waits until the supplied assertion succeeds, retrying whenever any observed grain activity occurs.
    /// </summary>
    /// <param name="assertion">The assertion callback to evaluate.</param>
    /// <param name="timeout">
    /// The maximum time to wait. When <see langword="null"/>, <see cref="WaitForAssertionDefaults.Timeout"/> is used.
    /// Timeout enforcement is skipped while a debugger is attached.
    /// </param>
    /// <param name="ct">A token that cancels the wait.</param>
    /// <returns>A task that completes when the assertion succeeds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assertion"/> is <see langword="null"/>.</exception>
    /// <exception cref="WaitForAssertionTimeoutException">
    /// Thrown when the assertion does not succeed before the timeout expires. The <see cref="Exception.InnerException"/>
    /// contains the last assertion failure.
    /// </exception>
    /// <example>
    /// <code><![CDATA[
    /// await collector.WaitForAssertionAsync(async () =>
    /// {
    ///     Assert.Equal("ready", await grain.GetValueAsync());
    /// });
    /// ]]></code>
    /// </example>
    public Task WaitForAssertionAsync(
        Func<Task> assertion,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        => WaitForAssertionAsyncCore<bool>(
            assertion is null ? null! : () => WrapTask(assertion),
            filter: null,
            timeout,
            grainId: null,
            ct);

    /// <summary>
    /// Waits until the supplied assertion succeeds, retrying whenever any observed grain activity occurs.
    /// </summary>
    /// <typeparam name="TResult">The assertion result type.</typeparam>
    /// <param name="assertion">The assertion callback to evaluate.</param>
    /// <param name="timeout">
    /// The maximum time to wait. When <see langword="null"/>, <see cref="WaitForAssertionDefaults.Timeout"/> is used.
    /// Timeout enforcement is skipped while a debugger is attached.
    /// </param>
    /// <param name="ct">A token that cancels the wait.</param>
    /// <returns>The value returned by the successful assertion callback.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assertion"/> is <see langword="null"/>.</exception>
    /// <exception cref="WaitForAssertionTimeoutException">
    /// Thrown when the assertion does not succeed before the timeout expires. The <see cref="Exception.InnerException"/>
    /// contains the last assertion failure.
    /// </exception>
    /// <example>
    /// <code><![CDATA[
    /// var number = await collector.WaitForAssertionAsync(async () =>
    /// {
    ///     var value = await grain.GetNumberAsync();
    ///     Assert.True(value > 0);
    ///     return value;
    /// });
    /// ]]></code>
    /// </example>
    public Task<TResult> WaitForAssertionAsync<TResult>(
        Func<Task<TResult>> assertion,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        => WaitForAssertionAsyncCore<TResult>(
            assertion is null ? null! : () => new ValueTask<TResult>(assertion()),
            filter: null,
            timeout,
            grainId: null,
            ct);

    /// <summary>
    /// Waits until the supplied assertion succeeds, retrying only when activity from the specified grain occurs.
    /// </summary>
    /// <typeparam name="TGrain">The grain interface type.</typeparam>
    /// <param name="grain">The grain whose activity should trigger retries.</param>
    /// <param name="assertion">The assertion callback to evaluate.</param>
    /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, <see cref="WaitForAssertionDefaults.Timeout"/> is used.</param>
    /// <param name="ct">A token that cancels the wait.</param>
    /// <returns>A task that completes when the assertion succeeds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="grain"/> or <paramref name="assertion"/> is <see langword="null"/>.</exception>
    /// <exception cref="WaitForAssertionTimeoutException">Thrown when the assertion does not succeed before the timeout expires.</exception>
    public Task WaitForAssertionAsync<TGrain>(
        TGrain grain,
        Func<Task> assertion,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(grain);
        return WaitForAssertionAsyncCore<bool>(
            assertion is null ? null! : () => WrapTask(assertion),
            CreateActivityFilter(grain.GetGrainId()),
            timeout,
            grain.GetGrainId(),
            ct);
    }

    /// <summary>
    /// Waits until the supplied assertion succeeds, retrying only when activity from the specified grain occurs.
    /// </summary>
    public Task<TResult> WaitForAssertionAsync<TGrain, TResult>(
        TGrain grain,
        Func<Task<TResult>> assertion,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(grain);
        return WaitForAssertionAsyncCore<TResult>(
            assertion is null ? null! : () => new ValueTask<TResult>(assertion()),
            CreateActivityFilter(grain.GetGrainId()),
            timeout,
            grain.GetGrainId(),
            ct);
    }

    /// <summary>
    /// Waits until the supplied assertion succeeds, retrying only when activity from the specified grain occurs.
    /// </summary>
    public Task WaitForAssertionAsync<TGrain>(
        TGrain grain,
        Func<TGrain, Task> assertion,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(grain);
        return WaitForAssertionAsyncCore<bool>(
            assertion is null ? null! : () => WrapTask(() => assertion(grain)),
            CreateActivityFilter(grain.GetGrainId()),
            timeout,
            grain.GetGrainId(),
            ct);
    }

    /// <summary>
    /// Waits until the supplied assertion succeeds, retrying only when activity from the specified grain occurs.
    /// </summary>
    public Task<TResult> WaitForAssertionAsync<TGrain, TResult>(
        TGrain grain,
        Func<TGrain, Task<TResult>> assertion,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(grain);
        return WaitForAssertionAsyncCore<TResult>(
            assertion is null ? null! : () => new ValueTask<TResult>(assertion(grain)),
            CreateActivityFilter(grain.GetGrainId()),
            timeout,
            grain.GetGrainId(),
            ct);
    }

    /// <summary>
    /// Waits for a storage operation matching the supplied predicate.
    /// </summary>
    /// <param name="predicate">Returns <see langword="true"/> when the expected operation has been observed.</param>
    /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, <see cref="WaitForAssertionDefaults.Timeout"/> is used.</param>
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
            (out ChannelReader<StorageOperation> reader) => SubscribeStorageOperations(out reader),
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
            (out ChannelReader<StorageOperation> reader) => SubscribeStorageOperations(out reader, operation => operation.GrainId == grain.GetGrainId()),
            timeout,
            grain.GetGrainId(),
            ct);
    }

    /// <summary>
    /// Waits for an incoming grain call matching the supplied predicate.
    /// </summary>
    /// <param name="predicate">Returns <see langword="true"/> when the expected incoming call has been observed.</param>
    /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, <see cref="WaitForAssertionDefaults.Timeout"/> is used.</param>
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
            (out ChannelReader<IIncomingGrainCallContext> reader) => SubscribeGrainCalls(out reader),
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
            (out ChannelReader<IIncomingGrainCallContext> reader) => SubscribeGrainCalls(out reader, context => context.TargetId == grain.GetGrainId()),
            timeout,
            grain.GetGrainId(),
            ct);
    }

    internal void OnStorageOperation(StorageOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

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

    private async Task<TResult> WaitForAssertionAsyncCore<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        GrainId? grainId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assertion);

        using var subscription = SubscribeActivities(out var reader, filter);
        var stopwatch = Stopwatch.StartNew();
        Exception? lastFailure = null;

        using (RequestContextScope.ForAssertion())
        {
            try
            {
                return await assertion().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastFailure = ex;
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
                        lastFailure = ex;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw CreateTimeoutException(lastFailure, grainId, stopwatch.Elapsed);
        }

        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("The activity stream completed unexpectedly.");
    }

    private async Task WaitForPredicateAsyncCore<T>(
        Func<T, bool> predicate,
        SubscribeDelegate<T> subscribe,
        TimeSpan? timeout,
        GrainId? grainId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(subscribe);

        using var subscription = subscribe(out var reader);
        using var timeoutCts = CreateTimeoutCancellationTokenSource(timeout, ct);
        var effectiveToken = timeoutCts?.Token ?? ct;
        var stopwatch = Stopwatch.StartNew();

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
        throw new InvalidOperationException("The event stream completed unexpectedly.");
    }

    private IDisposable SubscribeActivities(out ChannelReader<GrainActivity> reader, Predicate<GrainActivity>? filter = null)
    {
        var channel = CreateChannel<GrainActivity>();
        var subscriber = new ActivitySubscriber(channel, filter);
        lock (activitySubscribersLock)
        {
            activitySubscribers = [.. activitySubscribers, subscriber];
        }

        reader = channel.Reader;
        return new ActivitySubscription(this, subscriber);
    }

    private IDisposable SubscribeStorageOperations(out ChannelReader<StorageOperation> reader, Predicate<StorageOperation>? filter = null)
    {
        var channel = CreateChannel<StorageOperation>();
        var subscriber = new StorageSubscriber(channel, filter);
        lock (storageSubscribersLock)
        {
            storageSubscribers = [.. storageSubscribers, subscriber];
        }

        reader = channel.Reader;
        return new StorageSubscription(this, subscriber);
    }

    private IDisposable SubscribeGrainCalls(out ChannelReader<IIncomingGrainCallContext> reader, Predicate<IIncomingGrainCallContext>? filter = null)
    {
        var channel = CreateChannel<IIncomingGrainCallContext>();
        var subscriber = new GrainCallSubscriber(channel, filter);
        lock (grainCallSubscribersLock)
        {
            grainCallSubscribers = [.. grainCallSubscribers, subscriber];
        }

        reader = channel.Reader;
        return new GrainCallSubscription(this, subscriber);
    }

    private void PublishActivity(GrainActivity activity)
    {
        var snapshot = activitySubscribers;
        foreach (var subscriber in snapshot)
        {
            if (subscriber.Filter is not null && !subscriber.Filter(activity))
            {
                continue;
            }

            if (!subscriber.Channel.Writer.TryWrite(activity))
            {
                throw new InvalidOperationException("A grain activity subscriber channel is full.");
            }
        }
    }

    private void PublishStorageOperation(StorageOperation operation)
    {
        var snapshot = storageSubscribers;
        foreach (var subscriber in snapshot)
        {
            if (subscriber.Filter is not null && !subscriber.Filter(operation))
            {
                continue;
            }

            if (!subscriber.Channel.Writer.TryWrite(operation))
            {
                throw new InvalidOperationException("A storage operation subscriber channel is full.");
            }
        }
    }

    private void PublishGrainCall(IIncomingGrainCallContext context)
    {
        var snapshot = grainCallSubscribers;
        foreach (var subscriber in snapshot)
        {
            if (subscriber.Filter is not null && !subscriber.Filter(context))
            {
                continue;
            }

            if (!subscriber.Channel.Writer.TryWrite(context))
            {
                throw new InvalidOperationException("A grain call subscriber channel is full.");
            }
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

    private static Predicate<GrainActivity> CreateActivityFilter(GrainId grainId) => activity => activity.GrainId == grainId;

    private static CancellationTokenSource? CreateTimeoutCancellationTokenSource(TimeSpan? timeout, CancellationToken ct)
    {
        if (Debugger.IsAttached)
        {
            return null;
        }

        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? WaitForAssertionDefaults.Timeout);
        return timeoutCts;
    }

    private static WaitForAssertionTimeoutException CreateTimeoutException(Exception? innerException, GrainId? grainId, TimeSpan elapsed)
    {
        var message = grainId is null
            ? $"Timed out waiting for Orleans test activity after {elapsed}."
            : $"Timed out waiting for Orleans test activity for grain '{grainId}' after {elapsed}.";

        return new WaitForAssertionTimeoutException(message, innerException, grainId, elapsed);
    }

    private static Channel<T> CreateChannel<T>() =>
        Channel.CreateBounded<T>(new BoundedChannelOptions(SubscriberChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });

    private static async ValueTask<bool> WrapTask(Func<Task> assertion)
    {
        await assertion().ConfigureAwait(false);
        return true;
    }

    private delegate IDisposable SubscribeDelegate<T>(out ChannelReader<T> reader);

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
}
