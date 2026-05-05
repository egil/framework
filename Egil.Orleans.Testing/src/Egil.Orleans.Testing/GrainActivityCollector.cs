using System.Collections.Concurrent;
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
/// <para>
/// The standard <c>WaitForAssertionAsync</c> overloads retry assertions based on observed grain activity.
/// The <c>Get*Async</c> methods return <see cref="IAsyncEnumerable{T}"/> feeds that can be composed with
/// LINQ operators such as <c>Where</c>, <c>Take</c>, and <c>Select</c> for fine-grained event observation.
/// </para>
/// <para>
/// <see cref="GrainActivityCollector"/> also implements <see cref="IGrainActivityWaiter"/>, so fixtures can
/// forward a single low-level wait primitive and expose the same wait surface through
/// <see cref="GrainActivityWaiterExtensions"/> without forcing callers through a <c>fixture.Collector</c> hop.
/// </para>
/// <para>
/// The collector implements <see cref="IDisposable"/>. Calling <see cref="Dispose"/> completes all
/// active subscriber channels (causing any pending <c>ReadAllAsync</c> loops to terminate), removes
/// every subscription, and clears the recent-event history. After disposal, <c>WaitFor*</c> and
/// <c>Get*Async</c> methods throw <see cref="ObjectDisposedException"/>, while internal publish
/// methods become silent no-ops to avoid cascading failures during shutdown.
/// </para>
/// </remarks>
public sealed class GrainActivityCollector : IGrainActivityWaiter, IDisposable
{
    private const int RecentEventHistoryCapacity = 256;

    // All activity is written to the ring buffer (recentActivity) and fanned out
    // to subscriber channels under activityLock. This ensures that subscribe+snapshot
    // (for includeExisting) and publish are mutually exclusive, eliminating
    // duplicate or missed events.
    private readonly Lock activityLock = new();
    private readonly ConcurrentDictionary<Channel<GrainActivity>, object?> subscribers = [];
    private readonly Queue<GrainActivity> recentActivity = new();
    private volatile bool disposed;

    /// <inheritdoc cref="IGrainActivityWaiter.WaitForAssertionAsync{TResult}"/>
    [StackTraceHidden]
    public Task<TResult> WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => WaitForAssertionAsyncCore(assertion, filter, timeout, grainId: null, cancellationToken);

    /// <summary>
    /// Returns a live feed of all grain activity observed by the collector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The subscription begins when enumeration starts (i.e. when the first <c>MoveNextAsync</c> call is made).
    /// When <paramref name="includeExisting"/> is <see langword="false"/> (the default), only activity that
    /// occurs <b>after</b> enumeration begins is delivered. When <see langword="true"/>, the most recent
    /// activity history (up to 256 events) is replayed before live events.
    /// </para>
    /// <para>
    /// The feed uses an unbounded buffer so slow consumers never lose events. The feed completes
    /// when <paramref name="cancellationToken"/> is cancelled or the caller stops enumerating.
    /// </para>
    /// </remarks>
    /// <param name="includeExisting">
    /// When <see langword="true"/>, replays the recent activity history before live events.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <param name="cancellationToken">A token that stops the feed and removes the subscription.</param>
    /// <returns>An async enumerable that yields grain activity as it occurs.</returns>
    public async IAsyncEnumerable<GrainActivity> GetGrainActivityAsync(
        bool includeExisting = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = CreateChannel();

        if (includeExisting)
        {
            // Subscribe and prefill the channel with history under the same lock
            // so that Publish cannot interleave — events before the lock are
            // written to the channel as history, events after the lock are
            // written by Publish. No duplicates, no gaps, no array copy.
            lock (activityLock)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                subscribers[channel] = null;
                foreach (var item in recentActivity)
                {
                    channel.Writer.TryWrite(item);
                }
            }
        }
        else
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            subscribers[channel] = null;
        }

        try
        {
            await foreach (var activity in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return activity;
            }
        }
        finally
        {
            subscribers.TryRemove(channel, out _);
            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Returns a live feed of all storage operations observed by the collector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Internally delegates to <see cref="GetGrainActivityAsync"/> and filters to storage activity.
    /// Use LINQ operators such as <c>Where</c>, <c>Take</c>, and <c>Select</c> for further composition.
    /// </para>
    /// <para><b>Coupling risk:</b> This method exposes low-level persistence implementation details.
    /// Tests using this feed are tightly coupled to storage providers, write timing, and persistence strategy.
    /// Prefer <c>WaitForAssertionAsync</c> when you can express the expected behavior through the grain's public API.</para>
    /// </remarks>
    /// <param name="includeExisting">
    /// When <see langword="true"/>, replays the recent activity history before live events.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <param name="cancellationToken">A token that stops the feed and removes the subscription.</param>
    /// <returns>An async enumerable that yields storage operations as they occur.</returns>
    public IAsyncEnumerable<StorageOperation> GetStorageOperationsAsync(
        bool includeExisting = false,
        CancellationToken cancellationToken = default)
        => GetGrainActivityAsync(includeExisting, cancellationToken)
            .Where(a => a.IsStorageActivity)
            .Select(a => a.StorageOperation!.Value);

    /// <summary>
    /// Returns a live feed of storage operations for the specified grain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Delegates to the global <see cref="GetStorageOperationsAsync(bool, CancellationToken)"/> overload
    /// and filters by grain identity.
    /// </para>
    /// <para><b>Coupling risk:</b> This method exposes low-level persistence implementation details.
    /// Tests using this feed are tightly coupled to storage providers, write timing, and persistence strategy.
    /// Prefer <c>WaitForAssertionAsync</c> when you can express the expected behavior through the grain's public API.</para>
    /// </remarks>
    /// <typeparam name="TGrain">The grain interface type.</typeparam>
    /// <param name="grain">The grain whose storage operations should be included in the feed.</param>
    /// <param name="includeExisting">
    /// When <see langword="true"/>, replays the recent activity history before live events.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <param name="cancellationToken">A token that stops the feed and removes the subscription.</param>
    /// <returns>An async enumerable that yields storage operations for the specified grain as they occur.</returns>
    public IAsyncEnumerable<StorageOperation> GetStorageOperationsAsync<TGrain>(
        TGrain grain,
        bool includeExisting = false,
        CancellationToken cancellationToken = default)
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(grain);
        var grainId = grain.GetGrainId();
        return GetStorageOperationsAsync(includeExisting, cancellationToken)
            .Where(op => op.GrainId == grainId);
    }

    /// <summary>
    /// Returns a live feed of all incoming grain calls observed by the collector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Internally delegates to <see cref="GetGrainActivityAsync"/> and filters to grain call activity.
    /// Use LINQ operators such as <c>Where</c>, <c>Take</c>, and <c>Select</c> for further composition.
    /// </para>
    /// <para><b>Coupling risk:</b> This method exposes low-level call flow rather than externally observable grain behavior.
    /// Tests using this feed are tightly coupled to internal call structure and can break when implementation details change.
    /// Prefer <c>WaitForAssertionAsync</c> for behavior-first assertions.</para>
    /// </remarks>
    /// <param name="includeExisting">
    /// When <see langword="true"/>, replays the recent activity history before live events.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <param name="cancellationToken">A token that stops the feed and removes the subscription.</param>
    /// <returns>An async enumerable that yields incoming grain call contexts as they occur.</returns>
    public IAsyncEnumerable<IIncomingGrainCallContext> GetGrainCallsAsync(
        bool includeExisting = false,
        CancellationToken cancellationToken = default)
        => GetGrainActivityAsync(includeExisting, cancellationToken)
            .Where(a => a.IsGrainCall)
            .Select(a => a.GrainCallContext!);

    /// <summary>
    /// Returns a live feed of incoming grain calls for the specified grain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Delegates to the global <see cref="GetGrainCallsAsync(bool, CancellationToken)"/> overload
    /// and filters by grain identity.
    /// </para>
    /// <para><b>Coupling risk:</b> This method exposes low-level call flow rather than externally observable grain behavior.
    /// Tests using this feed are tightly coupled to internal call structure and can break when implementation details change.
    /// Prefer <c>WaitForAssertionAsync</c> for behavior-first assertions.</para>
    /// </remarks>
    /// <typeparam name="TGrain">The grain interface type.</typeparam>
    /// <param name="grain">The grain whose incoming calls should be included in the feed.</param>
    /// <param name="includeExisting">
    /// When <see langword="true"/>, replays the recent activity history before live events.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <param name="cancellationToken">A token that stops the feed and removes the subscription.</param>
    /// <returns>An async enumerable that yields incoming grain call contexts for the specified grain as they occur.</returns>
    public IAsyncEnumerable<IIncomingGrainCallContext> GetGrainCallsAsync<TGrain>(
        TGrain grain,
        bool includeExisting = false,
        CancellationToken cancellationToken = default)
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(grain);
        var grainId = grain.GetGrainId();
        return GetGrainCallsAsync(includeExisting, cancellationToken)
            .Where(ctx => ctx.TargetId == grainId);
    }

    /// <summary>
    /// Completes all active subscriber channels, removes every subscription, and clears
    /// the recent-event history. After disposal, <c>WaitFor*</c> and <c>Get*Async</c>
    /// methods throw <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        lock (activityLock)
        {
            disposed = true;
            recentActivity.Clear();
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Key.Writer.TryComplete();
        }
    }

    internal void OnStorageOperation(StorageOperation operation)
    {
        Publish(new GrainActivity(
            operation.GrainId,
            operation.Kind switch
            {
                StorageOperationKind.Clear => GrainActivityKind.StorageClear,
                StorageOperationKind.Read => GrainActivityKind.StorageRead,
                StorageOperationKind.Write => GrainActivityKind.StorageWrite,
                _ => throw new UnreachableException(),
            },
            DateTimeOffset.UtcNow)
        {
            StorageOperation = operation,
        });
    }

    internal void OnGrainCall(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Publish(new GrainActivity(context.TargetId, GrainActivityKind.GrainCall, DateTimeOffset.UtcNow)
        {
            GrainCallContext = context,
        });
    }

    private void Publish(GrainActivity activity)
    {
        if (disposed)
        {
            return;
        }

        lock (activityLock)
        {
            if (disposed)
            {
                return;
            }

            EnqueueRecentEvent(activity);

            foreach (var subscriber in subscribers)
            {
                subscriber.Key.Writer.TryWrite(activity);
            }
        }
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
        var stopwatch = Stopwatch.StartNew();

        // Register the subscription eagerly — before the first assertion — so that
        // no activity events are lost between the initial assertion attempt and the
        // start of the retry loop.
        var channel = CreateChannel();
        ObjectDisposedException.ThrowIf(disposed, this);
        subscribers[channel] = null;

        try
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

            using var timeoutCts = CreateTimeoutCancellationTokenSource(timeout, ct);
            var effectiveToken = timeoutCts?.Token ?? ct;

            IAsyncEnumerable<GrainActivity> stream = channel.Reader.ReadAllAsync(effectiveToken);
            if (filter is not null)
            {
                stream = stream.Where(a => filter(a));
            }

            try
            {
                await foreach (var _ in stream.ConfigureAwait(false))
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
        finally
        {
            subscribers.TryRemove(channel, out _);
            channel.Writer.TryComplete();
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

    private static Channel<GrainActivity> CreateChannel() =>
        Channel.CreateUnbounded<GrainActivity>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    private void EnqueueRecentEvent(GrainActivity item)
    {
        recentActivity.Enqueue(item);
        while (recentActivity.Count > RecentEventHistoryCapacity)
        {
            recentActivity.Dequeue();
        }
    }
}