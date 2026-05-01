using System.Globalization;

namespace Egil.Orleans.Testing;

/// <summary>
/// Provides default configuration for <c>WaitFor*</c> APIs.
/// </summary>
public static class WaitForAssertionDefaults
{
    /// <summary>
    /// Gets the default wait timeout used when callers do not supply one explicitly.
    /// </summary>
    /// <remarks>
    /// The timeout is read once from the <c>WAIT_FOR_ASSERTION_TIMEOUT_SECONDS</c> environment variable.
    /// If the variable is missing, invalid, or less than or equal to zero, the default is 5 seconds.
    /// Individual wait methods may choose to bypass timeout enforcement when a debugger is attached.
    /// </remarks>
    public static readonly TimeSpan Timeout = LoadTimeout();

    private static TimeSpan LoadTimeout()
    {
        const double defaultTimeoutSeconds = 5;
        var configuredTimeout = Environment.GetEnvironmentVariable("WAIT_FOR_ASSERTION_TIMEOUT_SECONDS");

        return double.TryParse(configuredTimeout, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromSeconds(defaultTimeoutSeconds);
    }
}
