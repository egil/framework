namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Configuration for an event stream, separate from event handling logic.
/// Contains all the stream-related policies and behaviors.
/// </summary>
/// <typeparam name="TEvent">The base event type for this stream</typeparam>
public sealed class EventStreamConfiguration<TEvent>
    where TEvent : class
{
    public required string StreamName { get; init; }
    public bool EnableDeduplication { get; init; } = false;
    public IEventRetentionPolicy RetentionPolicy { get; init; } = EventRetentionPolicies.KeepAll();
    public Func<TEvent, bool> ShouldStoreEvent { get; init; } = _ => true;
    public Func<TEvent, string?> GetDeduplicationId { get; init; } = _ => null;
}

/// <summary>
/// Mutable outbox for accumulating events during event processing.
/// A new instance is created for each event application.
/// </summary>
/// <typeparam name="TOutboxEvent">The base type for outbox events</typeparam>
public sealed class EventOutbox<TOutboxEvent>
    where TOutboxEvent : class
{
    private readonly List<OutboxEvent> events = new();
    private readonly List<OrleansStreamTarget<TOutboxEvent>> streamTargets = new();
    private readonly List<CustomOutboxAction> customActions = new();

    /// <summary>
    /// Gets the outbox events that have been added.
    /// </summary>
    public IReadOnlyList<OutboxEvent> Events => events;

    /// <summary>
    /// Gets the Orleans stream targets that have been added.
    /// </summary>
    public IReadOnlyList<OrleansStreamTarget<TOutboxEvent>> StreamTargets => streamTargets;

    /// <summary>
    /// Gets the custom actions that have been added.
    /// </summary>
    public IReadOnlyList<CustomOutboxAction> CustomActions => customActions;

    /// <summary>
    /// Adds an outbox event to be published.
    /// </summary>
    /// <param name="outboxEvent">The event to add</param>
    public void Add(OutboxEvent outboxEvent)
    {
        events.Add(outboxEvent);
    }

    /// <summary>
    /// Adds multiple outbox events to be published.
    /// </summary>
    /// <param name="outboxEvents">The events to add</param>
    public void AddRange(IEnumerable<OutboxEvent> outboxEvents)
    {
        events.AddRange(outboxEvents);
    }

    /// <summary>
    /// Helper method to create and add an outbox event for publishing.
    /// </summary>
    /// <param name="event">The event to add to outbox</param>
    /// <param name="grainId">The grain ID that generated the event</param>
    /// <param name="targetStream">Optional target stream name</param>
    public void Add(TOutboxEvent @event, string grainId, string? targetStream = null)
    {
        var outboxEvent = new OutboxEvent(
            Id: Guid.NewGuid().ToString(),
            GrainId: grainId,
            Event: @event,
            CreatedAt: DateTime.UtcNow,
            EventTypeName: @event.GetType().FullName!,
            TargetStream: targetStream ?? @event.GetType().Name);
        
        Add(outboxEvent);
    }

    /// <summary>
    /// Adds an event to be sent to an Orleans stream.
    /// </summary>
    /// <param name="event">The event to send</param>
    /// <param name="streamNamespace">The stream namespace</param>
    /// <param name="streamKey">The stream key/ID</param>
    public void SendToStream(TOutboxEvent @event, string streamNamespace, string streamKey)
    {
        streamTargets.Add(new OrleansStreamTarget<TOutboxEvent>(
            Event: @event,
            StreamNamespace: streamNamespace,
            StreamKey: streamKey));
    }

    /// <summary>
    /// Adds a custom async action to be executed during outbox processing.
    /// </summary>
    /// <param name="action">The async action to execute</param>
    /// <param name="description">Optional description for logging</param>
    public void AddCustomAction(Func<CancellationToken, ValueTask> action, string? description = null)
    {
        customActions.Add(new CustomOutboxAction(action, description));
    }

    /// <summary>
    /// Converts to an immutable Outbox for storage.
    /// </summary>
    /// <returns>An immutable Outbox containing all added events</returns>
    public Outbox<TOutboxEvent> ToImmutable()
    {
        return new Outbox<TOutboxEvent>(events, streamTargets, customActions);
    }
}

/// <summary>
/// Represents a target Orleans stream for outbox events.
/// </summary>
/// <typeparam name="TEvent">The event type</typeparam>
public sealed record OrleansStreamTarget<TEvent>(
    TEvent Event,
    string StreamNamespace,
    string StreamKey)
    where TEvent : class;

/// <summary>
/// Represents a custom async action to be executed during outbox processing.
/// </summary>
public sealed record CustomOutboxAction(
    Func<CancellationToken, ValueTask> Action,
    string? Description = null);

/// <summary>
/// Delegate for async event handling functions.
/// </summary>
/// <typeparam name="TEvent">The type of events this handler processes</typeparam>
/// <typeparam name="TProjection">The type of the projection</typeparam>
/// <typeparam name="TOutboxEvent">The type of outbox events</typeparam>
/// <param name="event">The event to apply</param>
/// <param name="projection">The current projection state</param>
/// <param name="outbox">Mutable outbox for accumulating events (optional for read-only handlers)</param>
/// <returns>The updated projection state</returns>
public delegate ValueTask<TProjection> EventHandlerDelegate<in TEvent, TProjection, TOutboxEvent>(
    TEvent @event, 
    TProjection projection, 
    EventOutbox<TOutboxEvent>? outbox = null)
    where TProjection : notnull
    where TOutboxEvent : class;

/// <summary>
/// Delegate for async event handling functions without outbox support.
/// Use this for read-only event handlers that don't produce side effects.
/// </summary>
/// <typeparam name="TEvent">The type of events this handler processes</typeparam>
/// <typeparam name="TProjection">The type of the projection</typeparam>
/// <param name="event">The event to apply</param>
/// <param name="projection">The current projection state</param>
/// <returns>The updated projection state</returns>
public delegate ValueTask<TProjection> EventHandlerDelegateReadOnly<in TEvent, TProjection>(TEvent @event, TProjection projection)
    where TProjection : notnull;

/// <summary>
/// Interface for applying events to projections with async support.
/// This interface only deals with the event application logic, not stream configuration.
/// Implementations can perform async work and use dependency injection.
/// </summary>
/// <typeparam name="TEvent">The type of events this handler processes</typeparam>
/// <typeparam name="TProjection">The type of the projection</typeparam>
/// <typeparam name="TOutboxEvent">The type of outbox events</typeparam>
public interface IEventHandler<in TEvent, TProjection, TOutboxEvent>
    where TProjection : notnull
    where TOutboxEvent : class
{
    /// <summary>
    /// Applies an event to the projection and optionally populates the outbox with derived events.
    /// This method can perform async work and access injected dependencies.
    /// </summary>
    /// <param name="event">The event to apply</param>
    /// <param name="projection">The current projection state</param>
    /// <param name="outbox">Mutable outbox for adding derived events (null for read-only handlers)</param>
    /// <returns>The updated projection state</returns>
    ValueTask<TProjection> ApplyEventAsync(TEvent @event, TProjection projection, EventOutbox<TOutboxEvent>? outbox = null);
}

/// <summary>
/// Result of processing an event, including the updated projection and any side effects.
/// </summary>
/// <typeparam name="TProjection">The type of the projection</typeparam>
/// <typeparam name="TOutboxEvent">The type of outbox events</typeparam>
public readonly record struct EventProcessingResult<TProjection, TOutboxEvent>
    where TProjection : notnull
    where TOutboxEvent : class
{
    /// <summary>
    /// The updated projection state after processing the event.
    /// </summary>
    public TProjection Projection { get; }
    
    /// <summary>
    /// The sequence number of the processed event.
    /// </summary>
    public long SequenceNumber { get; }
    
    /// <summary>
    /// The outbox events generated from processing.
    /// </summary>
    public Outbox<TOutboxEvent> Outbox { get; }

    /// <summary>
    /// Initializes a new instance of EventProcessingResult.
    /// </summary>
    public EventProcessingResult(TProjection projection, long sequenceNumber, Outbox<TOutboxEvent> outbox)
    {
        Projection = projection;
        SequenceNumber = sequenceNumber;
        Outbox = outbox;
    }
}

/// <summary>
/// Interface for event projections that can create their own default instances.
/// This ensures projections are always initialized properly.
/// </summary>
/// <typeparam name="TSelf">The projection type itself</typeparam>
public interface IEventProjection<out TSelf>
    where TSelf : IEventProjection<TSelf>
{
    /// <summary>
    /// Creates a default/empty instance of the projection.
    /// </summary>
    static abstract TSelf CreateDefault();
}
