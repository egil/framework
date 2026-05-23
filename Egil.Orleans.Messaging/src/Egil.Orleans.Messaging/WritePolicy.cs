namespace Egil.Orleans.Messaging;

/// <summary>
/// Controls how <see cref="IStateManager{T}.WriteAsync"/> handles optimistic concurrency.
/// </summary>
[GenerateSerializer]
[Alias("egil.orleans.messaging.WritePolicy")]
public enum WritePolicy
{
    /// <summary>
    /// Default. Uses the storage provider's ETag-based optimistic concurrency check.
    /// If another writer changed the state since the last read, the write fails with
    /// <c>InconsistentStateException</c>.
    /// </summary>
    Concurrent = 0,

    /// <summary>
    /// Nulls the ETag before writing, bypassing the provider's concurrency check.
    /// Use as a last-resort escape hatch when a grain needs to force-overwrite
    /// state regardless of concurrent modifications (e.g., admin repair operations).
    /// The recovery path and version stamping still run identically.
    /// </summary>
    Force = 1,
}
