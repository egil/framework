namespace Egil.Orleans.EventSourcing;

public interface IEventPublisher<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    ValueTask PublishAsync(IEnumerable<TEvent> @event, TProjection projection, IEventGrainContext context);
}
