using Orleans.Runtime;

namespace Egil.Orleans.Testing;

/// <summary>
/// Provides scoped helpers for <see cref="RequestContext"/> values used by the testing library.
/// </summary>
/// <remarks>
/// Each helper captures the previous value of the affected key and restores it on disposal.
/// If the key was previously unset, disposal removes it from <see cref="RequestContext"/>.
/// </remarks>
internal static class RequestContextScope
{
    /// <summary>
    /// The <see cref="RequestContext"/> key used to mark assertion machinery.
    /// </summary>
    public const string AssertionKey = "test-assertion";

    /// <summary>
    /// The <see cref="RequestContext"/> key used to correlate work with a test identifier.
    /// </summary>
    public const string TestIdKey = "test.id";

    /// <summary>
    /// Marks the wrapped scope as assertion machinery so collector internals can ignore self-triggered activity.
    /// </summary>
    /// <returns>
    /// A disposable scope that restores the previous <see cref="AssertionKey"/> value when disposed.
    /// </returns>
    public static IDisposable ForAssertion() => new Scope(AssertionKey, true);

    /// <summary>
    /// Stores a test correlation identifier in <see cref="RequestContext"/>.
    /// </summary>
    /// <param name="testId">The test correlation identifier.</param>
    /// <returns>
    /// A disposable scope that restores the previous <see cref="TestIdKey"/> value when disposed.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="testId"/> is <see langword="null"/>.</exception>
    public static IDisposable ForTest(string testId)
    {
        ArgumentNullException.ThrowIfNull(testId);
        return new Scope(TestIdKey, testId);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string key;
        private readonly object? previousValue;
        private readonly bool hadPreviousValue;
        private int disposed;

        public Scope(string key, object value)
        {
            this.key = key;
            previousValue = RequestContext.Get(key);
            hadPreviousValue = previousValue is not null;
            RequestContext.Set(key, value);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            if (hadPreviousValue)
            {
                RequestContext.Set(key, previousValue!);
            }
            else
            {
                RequestContext.Remove(key);
            }
        }
    }
}
