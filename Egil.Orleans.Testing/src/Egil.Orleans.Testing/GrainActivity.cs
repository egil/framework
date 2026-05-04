namespace Egil.Orleans.Testing;

/// <summary>
/// Represents a grain activity signal observed by the testing collector.
/// </summary>
/// <param name="grainId">The grain instance that produced the activity.</param>
/// <param name="kind">The kind of activity that occurred.</param>
/// <param name="timestamp">The time when the activity was observed.</param>
public readonly record struct GrainActivity(GrainId grainId, GrainActivityKind kind, DateTimeOffset timestamp)
{
    /// <summary>
    /// Gets the grain instance that produced the activity.
    /// </summary>
    public GrainId GrainId { get; } = grainId;

    /// <summary>
    /// Gets the kind of activity that occurred.
    /// </summary>
    public GrainActivityKind Kind { get; } = kind;

    /// <summary>
    /// Gets the time when the activity was observed.
    /// </summary>
    public DateTimeOffset Timestamp { get; } = timestamp;

    /// <summary>
    /// Gets the detailed storage operation when <see cref="Kind"/> is a storage activity.
    /// </summary>
    public StorageOperation? StorageOperation { get; init; }

    /// <summary>
    /// Gets the incoming grain call context when <see cref="Kind"/> is <see cref="GrainActivityKind.GrainCall"/>.
    /// </summary>
    public IIncomingGrainCallContext? GrainCallContext { get; init; }

    /// <summary>
    /// Gets a value indicating whether this activity represents a storage operation.
    /// When <see langword="true"/>, <see cref="StorageOperation"/> is guaranteed to have a value.
    /// </summary>
    [MemberNotNullWhen(true, nameof(StorageOperation))]
    public bool IsStorageActivity => StorageOperation.HasValue;

    /// <summary>
    /// Gets a value indicating whether this activity represents an incoming grain call.
    /// When <see langword="true"/>, <see cref="GrainCallContext"/> is guaranteed to be non-null.
    /// </summary>
    [MemberNotNullWhen(true, nameof(GrainCallContext))]
    public bool IsGrainCall => GrainCallContext is not null;
}
