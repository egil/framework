namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Base abstract class for event stream handlers with common functionality.
/// </summary>
/// <typeparam name="TEvent">The type of events this stream handles</typeparam>
/// <typeparam name="TProjection">The type of the projection</typeparam>
public abstract class EventStreamHandler<TEvent, TProjection> : IEventStreamHandler<TEvent, TProjection>
    where TProjection : notnull
{
    public abstract string StreamName { get; }
    
    public virtual bool EnableDeduplication => false;
    
    public virtual IEventRetentionPolicy RetentionPolicy => EventRetentionPolicies.KeepAll();
    
    public virtual bool ShouldStoreEvent(TEvent @event) => true;
    
    public virtual string? GetDeduplicationId(TEvent @event) => null;
    
    public abstract EventApplicationResult<TProjection> ApplyEvent(TProjection projection, TEvent @event);
    
    /// <summary>
    /// Helper method to create an outbox event for publishing.
    /// </summary>
    protected OutboxEvent CreateOutboxEvent(object @event, string grainId, string? targetStream = null)
    {
        return new OutboxEvent(
            Id: Guid.NewGuid().ToString(),
            GrainId: grainId,
            Event: @event,
            CreatedAt: DateTime.UtcNow,
            EventTypeName: @event.GetType().FullName!,
            TargetStream: targetStream ?? @event.GetType().Name);
    }
}
