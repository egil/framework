namespace Egil.Orleans.Testing;

/// <summary>
/// Represents a detailed grain storage operation observed by the testing collector.
/// </summary>
/// <param name="kind">The kind of storage operation that occurred.</param>
/// <param name="grainId">The grain instance whose state was accessed.</param>
/// <param name="storageName">The Orleans storage provider name.</param>
/// <param name="stateName">The state name passed to the storage provider.</param>
/// <param name="etag">The storage ETag, when available.</param>
/// <param name="state">The state payload involved in the operation.</param>
public readonly record struct StorageOperation(
    StorageOperationKind kind,
    GrainId grainId,
    string storageName,
    string stateName,
    string? etag,
    object? state)
{
    /// <summary>
    /// Gets the kind of storage operation that occurred.
    /// </summary>
    public StorageOperationKind Kind { get; } = kind;

    /// <summary>
    /// Gets the grain instance whose state was accessed.
    /// </summary>
    public GrainId GrainId { get; } = grainId;

    /// <summary>
    /// Gets the Orleans storage provider name.
    /// </summary>
    public string StorageName { get; } = storageName;

    /// <summary>
    /// Gets the state name passed to the storage provider.
    /// </summary>
    public string StateName { get; } = stateName;

    /// <summary>
    /// Gets the storage ETag, when available.
    /// </summary>
    public string? Etag { get; } = etag;

    /// <summary>
    /// Gets the state payload involved in the operation.
    /// </summary>
    public object? State { get; } = state;
}
