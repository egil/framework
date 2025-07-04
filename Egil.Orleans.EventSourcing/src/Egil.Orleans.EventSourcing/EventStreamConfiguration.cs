namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Configuration for an event stream, defining deduplication and retention policies.
/// </summary>
public sealed class EventStreamConfiguration
{
    /// <summary>
    /// Gets or sets the name of the event stream.
    /// </summary>
    public required string StreamName { get; init; }

    /// <summary>
    /// Gets or sets whether deduplication by event ID is enabled for this stream.
    /// </summary>
    public bool EnableDeduplicationById { get; init; } = false;

    /// <summary>
    /// Gets or sets the retention policy for events in this stream.
    /// </summary>
    public EventRetentionPolicy RetentionPolicy { get; init; } = EventRetentionPolicy.KeepAll();

    /// <summary>
    /// Gets or sets a predicate to determine if an event should be stored in this stream.
    /// </summary>
    public Func<object, bool> ShouldStoreEvent { get; init; } = _ => true;

    /// <summary>
    /// Gets or sets a function to extract the deduplication ID from an event.
    /// Only used when EnableDeduplicationById is true.
    /// </summary>
    public Func<object, string?>? GetEventId { get; init; }
}
