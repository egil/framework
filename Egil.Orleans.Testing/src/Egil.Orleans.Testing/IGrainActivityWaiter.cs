using System.Globalization;

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
    /// Gets or sets the default timeout used by wait helpers when callers do not supply one explicitly.
    /// </summary>
    /// <remarks>
    /// The initial value is read once from the <c>WAIT_FOR_ASSERTION_TIMEOUT_SECONDS</c> environment variable.
    /// If the variable is missing, invalid, or less than or equal to zero, the default is 5 seconds.
    /// Callers may replace the value at runtime to configure a different process-wide default.
    /// </remarks>
    static TimeSpan DefaultWaitTimeout
    {
        get => DefaultWaitTimeoutState.Value;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Default wait timeout must be greater than zero.");
            }

            DefaultWaitTimeoutState.Value = value;
        }
    }

    /// <summary>
    /// Waits until the supplied assertion succeeds, retrying whenever the matching grain activity signal is observed.
    /// </summary>
    /// <typeparam name="TResult">The assertion result type.</typeparam>
    /// <param name="assertion">The assertion callback to evaluate.</param>
    /// <param name="filter">An optional activity filter that limits which signals trigger retries.</param>
    /// <param name="timeout">The maximum time to wait. When <see langword="null"/>, <see cref="DefaultWaitTimeout"/> is used.</param>
    /// <param name="ct">A token that cancels the wait.</param>
    /// <returns>The value returned by the successful assertion callback.</returns>
    Task<TResult> WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken ct);

    internal static TimeSpan LoadDefaultWaitTimeout()
    {
        const double defaultTimeoutSeconds = 5;
        var configuredTimeout = Environment.GetEnvironmentVariable("WAIT_FOR_ASSERTION_TIMEOUT_SECONDS");

        return double.TryParse(configuredTimeout, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromSeconds(defaultTimeoutSeconds);
    }

    private static class DefaultWaitTimeoutState
    {
        public static TimeSpan Value = LoadDefaultWaitTimeout();
    }
}
