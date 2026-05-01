namespace Egil.Orleans.Testing;

/// <summary>
/// The exception that is thrown when a wait-for assertion does not succeed before the timeout expires.
/// </summary>
/// <remarks>
/// The <see cref="Exception.InnerException"/> typically contains the last assertion failure encountered before the timeout.
/// When the timeout comes from a failed assertion retry loop, the visible stack trace is based on that last assertion failure
/// so the first frames point back to the user's assertion code.
/// The optional <see cref="GrainId"/> and <see cref="Elapsed"/> properties provide additional troubleshooting context.
/// </remarks>
public sealed class WaitForAssertionTimeoutException : Exception, ITestTimeoutException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WaitForAssertionTimeoutException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public WaitForAssertionTimeoutException(string message)
        : this(message, innerException: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WaitForAssertionTimeoutException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The last assertion failure encountered before the timeout, when available.</param>
    /// <param name="grainId">The grain instance associated with the timed-out wait, when available.</param>
    /// <param name="elapsed">The elapsed time before the timeout was raised, when available.</param>
    public WaitForAssertionTimeoutException(
        string message,
        Exception? innerException,
        GrainId? grainId = null,
        TimeSpan? elapsed = null)
        : base(message, innerException)
    {
        GrainId = grainId;
        Elapsed = elapsed;
    }

    /// <summary>
    /// Gets the grain instance associated with the timed-out wait, when available.
    /// </summary>
    public GrainId? GrainId { get; }

    /// <summary>
    /// Gets the elapsed time before the timeout was raised, when available.
    /// </summary>
    public TimeSpan? Elapsed { get; }
}
