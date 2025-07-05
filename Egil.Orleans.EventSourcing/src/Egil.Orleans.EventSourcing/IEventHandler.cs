namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Interface for event handlers.
/// </summary>
public interface IEventHandler<in TEvent, TProjection>
{
    ValueTask<TProjection> HandleAsync(TEvent @event, TProjection projection, IEventGrainContext context);
}

public interface IEventPublisher<in TEvent, TProjection>
{
    ValueTask PublishAsync(IEnumerable<TEvent> @event, TProjection projection, IEventGrainContext context);
}
