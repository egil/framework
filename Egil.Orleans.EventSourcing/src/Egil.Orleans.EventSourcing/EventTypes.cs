namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Represents a stored event with metadata in an immutable structure.
/// </summary>
/// <param name="GrainId">The ID of the grain that owns this event</param>
/// <param name="StreamName">The name of the event stream</param>
/// <param name="SequenceNumber">The sequence number of the event</param>
/// <param name="Timestamp">When the event was stored</param>
/// <param name="Event">The actual event payload</param>
/// <param name="DeduplicationId">Optional ID for deduplication</param>
/// <param name="IsHandled">Whether this event has been processed</param>
/// <param name="EventTypeName">The full type name of the event</param>
public sealed record StoredEvent(
    string GrainId,
    string StreamName,
    long SequenceNumber,
    DateTime Timestamp,
    object Event,
    string? DeduplicationId,
    bool IsHandled,
    string EventTypeName);

/// <summary>
/// Represents an event in the outbox waiting to be published in an immutable structure.
/// </summary>
/// <param name="Id">Unique identifier for the outbox event</param>
/// <param name="GrainId">The ID of the grain that created this event</param>
/// <param name="Event">The event to be published</param>
/// <param name="CreatedAt">When the outbox event was created</param>
/// <param name="EventTypeName">The full type name of the event</param>
/// <param name="TargetStream">Optional target stream for publishing</param>
/// <param name="RetryCount">Number of retry attempts</param>
/// <param name="LastRetryAt">When the last retry occurred</param>
public sealed record OutboxEvent(
    string Id,
    string GrainId,
    object Event,
    DateTime CreatedAt,
    string EventTypeName,
    string? TargetStream = null,
    int RetryCount = 0,
    DateTime? LastRetryAt = null);

/// <summary>
/// Immutable collection of outbox events and actions.
/// </summary>
/// <typeparam name="TOutboxEvent">The type of outbox events</typeparam>
/// <param name="Events">The outbox events</param>
/// <param name="StreamTargets">The Orleans stream targets</param>
/// <param name="CustomActions">The custom async actions</param>
public sealed record Outbox<TOutboxEvent>(
    IReadOnlyList<OutboxEvent> Events,
    IReadOnlyList<OrleansStreamTarget<TOutboxEvent>> StreamTargets,
    IReadOnlyList<CustomOutboxAction> CustomActions)
    where TOutboxEvent : class
{
    public static readonly Outbox<TOutboxEvent> Empty = new(
        Array.Empty<OutboxEvent>(),
        Array.Empty<OrleansStreamTarget<TOutboxEvent>>(),
        Array.Empty<CustomOutboxAction>());
    
    public Outbox<TOutboxEvent> Add(OutboxEvent @event) => this with
    {
        Events = Events.Append(@event).ToArray()
    };
    
    public Outbox<TOutboxEvent> AddRange(IEnumerable<OutboxEvent> events) => this with
    {
        Events = Events.Concat(events).ToArray()
    };
}
