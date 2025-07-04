namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Represents the result of applying an event to a projection.
/// </summary>
/// <typeparam name="TProjection">The type of the projection</typeparam>
/// <param name="Projection">The updated projection state</param>
/// <param name="Outbox">The outbox events generated from applying the event</param>
public sealed record EventApplicationResult<TProjection>(
    TProjection Projection,
    Outbox Outbox)
    where TProjection : notnull;

/// <summary>
/// Base interface for event stream handlers that process specific event types.
/// </summary>
/// <typeparam name="TEvent">The type of events this stream handles</typeparam>
/// <typeparam name="TProjection">The type of the projection</typeparam>
public interface IEventStreamHandler<in TEvent, TProjection>
    where TProjection : notnull
{
    /// <summary>
    /// Gets the name of this event stream.
    /// </summary>
    string StreamName { get; }
    
    /// <summary>
    /// Determines if an event should be stored in this stream.
    /// </summary>
    bool ShouldStoreEvent(TEvent @event);
    
    /// <summary>
    /// Extracts the deduplication ID from an event, if applicable.
    /// </summary>
    string? GetDeduplicationId(TEvent @event);
    
    /// <summary>
    /// Determines if events in this stream should be deduplicated by ID.
    /// </summary>
    bool EnableDeduplication { get; }
    
    /// <summary>
    /// Gets the retention policy for events in this stream.
    /// </summary>
    IEventRetentionPolicy RetentionPolicy { get; }
    
    /// <summary>
    /// Applies an event to the projection and returns the updated projection and any outbox events.
    /// </summary>
    EventApplicationResult<TProjection> ApplyEvent(TProjection projection, TEvent @event);
}
