namespace Egil.Orleans.Testing;

/// <summary>
/// Represents a lightweight grain activity signal used to retry assertions.
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
}
