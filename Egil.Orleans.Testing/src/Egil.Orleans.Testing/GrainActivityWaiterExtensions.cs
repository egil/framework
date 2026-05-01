using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.Testing;

/// <summary>
/// Extension members that forward <c>WaitForAssertionAsync</c> overloads through an <see cref="IGrainActivityWaiter"/>.
/// </summary>
public static class GrainActivityWaiterExtensions
{
    extension(IGrainActivityWaiter waiter)
    {
        /// <summary>
        /// Waits until the supplied assertion succeeds, retrying whenever any observed grain activity occurs.
        /// </summary>
        /// <param name="assertion">The assertion callback to evaluate.</param>
        /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, <see cref="IGrainActivityWaiter.DefaultWaitTimeout"/> is used.</param>
        /// <param name="ct">A token that cancels the wait.</param>
        /// <returns>A task that completes when the assertion succeeds.</returns>
        [StackTraceHidden]
        public Task WaitForAssertionAsync(
            Func<Task> assertion,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
            => WaitForAssertionAsyncCore<bool>(
                waiter,
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
        /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, <see cref="IGrainActivityWaiter.DefaultWaitTimeout"/> is used.</param>
        /// <param name="ct">A token that cancels the wait.</param>
        /// <returns>The value returned by the successful assertion callback.</returns>
        [StackTraceHidden]
        public Task<TResult> WaitForAssertionAsync<TResult>(
            Func<Task<TResult>> assertion,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
            => WaitForAssertionAsyncCore<TResult>(
                waiter,
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
        /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, <see cref="IGrainActivityWaiter.DefaultWaitTimeout"/> is used.</param>
        /// <param name="ct">A token that cancels the wait.</param>
        /// <returns>A task that completes when the assertion succeeds.</returns>
        [StackTraceHidden]
        public Task WaitForAssertionAsync<TGrain>(
            TGrain grain,
            Func<Task> assertion,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
            where TGrain : IGrain
            => WaitForAssertionAsyncCore<bool>(
                waiter,
                assertion is null ? null! : () => WrapTask(assertion),
                CreateActivityFilter(grain),
                timeout,
                grain is null ? null : grain.GetGrainId(),
                ct);

        /// <summary>
        /// Waits until the supplied assertion succeeds, retrying only when activity from the specified grain occurs.
        /// </summary>
        /// <typeparam name="TGrain">The grain interface type.</typeparam>
        /// <typeparam name="TResult">The assertion result type.</typeparam>
        /// <param name="grain">The grain whose activity should trigger retries.</param>
        /// <param name="assertion">The assertion callback to evaluate.</param>
        /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, <see cref="IGrainActivityWaiter.DefaultWaitTimeout"/> is used.</param>
        /// <param name="ct">A token that cancels the wait.</param>
        /// <returns>The value returned by the successful assertion callback.</returns>
        [StackTraceHidden]
        public Task<TResult> WaitForAssertionAsync<TGrain, TResult>(
            TGrain grain,
            Func<Task<TResult>> assertion,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
            where TGrain : IGrain
            => WaitForAssertionAsyncCore<TResult>(
                waiter,
                assertion is null ? null! : () => new ValueTask<TResult>(assertion()),
                CreateActivityFilter(grain),
                timeout,
                grain is null ? null : grain.GetGrainId(),
                ct);

        /// <summary>
        /// Waits until the supplied assertion succeeds, retrying only when activity from the specified grain occurs.
        /// The grain reference is passed to the assertion callback for convenience.
        /// </summary>
        /// <typeparam name="TGrain">The grain interface type.</typeparam>
        /// <param name="grain">The grain whose activity should trigger retries.</param>
        /// <param name="assertion">The assertion callback that receives the grain reference.</param>
        /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, <see cref="IGrainActivityWaiter.DefaultWaitTimeout"/> is used.</param>
        /// <param name="ct">A token that cancels the wait.</param>
        /// <returns>A task that completes when the assertion succeeds.</returns>
        [StackTraceHidden]
        public Task WaitForAssertionAsync<TGrain>(
            TGrain grain,
            Func<TGrain, Task> assertion,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
            where TGrain : IGrain
            => WaitForAssertionAsyncCore<bool>(
                waiter,
                assertion is null ? null! : () => WrapTask(() => assertion(grain)),
                CreateActivityFilter(grain),
                timeout,
                grain is null ? null : grain.GetGrainId(),
                ct);

        /// <summary>
        /// Waits until the supplied assertion succeeds, retrying only when activity from the specified grain occurs.
        /// The grain reference is passed to the assertion callback for convenience.
        /// </summary>
        /// <typeparam name="TGrain">The grain interface type.</typeparam>
        /// <typeparam name="TResult">The assertion result type.</typeparam>
        /// <param name="grain">The grain whose activity should trigger retries.</param>
        /// <param name="assertion">The assertion callback that receives the grain reference.</param>
        /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, <see cref="IGrainActivityWaiter.DefaultWaitTimeout"/> is used.</param>
        /// <param name="ct">A token that cancels the wait.</param>
        /// <returns>The value returned by the successful assertion callback.</returns>
        [StackTraceHidden]
        public Task<TResult> WaitForAssertionAsync<TGrain, TResult>(
            TGrain grain,
            Func<TGrain, Task<TResult>> assertion,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
            where TGrain : IGrain
            => WaitForAssertionAsyncCore<TResult>(
                waiter,
                assertion is null ? null! : () => new ValueTask<TResult>(assertion(grain)),
                CreateActivityFilter(grain),
                timeout,
                grain is null ? null : grain.GetGrainId(),
                ct);

    }

    [StackTraceHidden]
    private static Task<TResult> WaitForAssertionAsyncCore<TResult>(
        IGrainActivityWaiter waiter,
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        GrainId? grainId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(waiter);
        var waitTask = waiter.WaitForAssertionAsync(assertion, filter, timeout, ct);
        if (grainId is null)
        {
            return waitTask;
        }

        return waitTask
            .ContinueWith(
                continuation =>
                {
                    if (continuation.IsFaulted
                        && continuation.Exception?.InnerException is WaitForAssertionTimeoutException timeoutException
                        && timeoutException.GrainId is null)
                    {
                        return Task.FromException<TResult>(AddGrainContext(timeoutException, grainId.Value));
                    }

                    return continuation;
                },
                TaskContinuationOptions.ExecuteSynchronously)
            .Unwrap();
    }

    private static Predicate<GrainActivity>? CreateActivityFilter<TGrain>(TGrain grain)
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(grain);
        var grainId = grain.GetGrainId();
        return activity => activity.GrainId == grainId;
    }

    private static async ValueTask<bool> WrapTask(Func<Task> assertion)
    {
        await assertion().ConfigureAwait(false);
        return true;
    }

    private static WaitForAssertionTimeoutException AddGrainContext(
        WaitForAssertionTimeoutException timeoutException,
        GrainId grainId)
    {
        var elapsed = timeoutException.Elapsed;
        var message = elapsed is { } value
            ? $"Timed out waiting for Orleans test activity for grain '{grainId}' after {value}."
            : timeoutException.Message;

        var exception = new WaitForAssertionTimeoutException(
            message,
            timeoutException.InnerException,
            grainId,
            elapsed);

        if (timeoutException.StackTrace is { } stackTrace)
        {
            ExceptionDispatchInfo.SetRemoteStackTrace(exception, stackTrace);
        }

        return exception;
    }
}
