namespace Egil.Orleans.Messaging.Streams;

/// <summary>
/// Optional metadata contract for stream sequence token implementations that
/// carry provider-specific diagnostics.
/// </summary>
public interface IStreamSequenceTokenMetadata
{
    /// <summary>
    /// Attempts to extract the broker-side enqueue time from the token.
    /// </summary>
    bool TryGetEnqueuedTime(out DateTimeOffset enqueuedTime);

    /// <summary>
    /// Attempts to extract the stream provider name from the token.
    /// </summary>
    bool TryGetProviderName([NotNullWhen(true)] out string? providerName);

    /// <summary>
    /// Attempts to extract the W3C traceparent value from the token.
    /// </summary>
    bool TryGetTraceParent([NotNullWhen(true)] out string? traceParent);
}