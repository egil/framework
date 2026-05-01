namespace Egil.Orleans.Testing;

/// <summary>
/// Exposes the low-level wait primitive used by <see cref="GrainActivityWaiterExtensions"/>.
/// </summary>
/// <remarks>
/// Implement this on test fixtures that already own a <see cref="GrainActivityCollector"/> instance.
/// In most cases the implementation is a one-line forwarder to the collector, and
/// <see cref="GrainActivityWaiterExtensions"/> then projects the ergonomic <c>WaitFor*</c> overloads onto the fixture.
/// </remarks>
public interface IGrainActivityWaiter
{
    /// <summary>
    /// Waits until the supplied assertion succeeds, retrying whenever the matching grain activity signal is observed.
    /// </summary>
    /// <typeparam name="TResult">The assertion result type.</typeparam>
    /// <param name="assertion">The assertion callback to evaluate.</param>
    /// <param name="filter">An optional activity filter that limits which signals trigger retries.</param>
    /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, the package default timeout is used.</param>
    /// <param name="ct">A token that cancels the wait.</param>
    /// <returns>The value returned by the successful assertion callback.</returns>
    Task<TResult> WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken ct);
}
