namespace Egil.Orleans.EventSourcing;

public interface IEventReactor<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    ValueTask ReactAsync(IEnumerable<TEvent> @event, TProjection projection, IEventReactContext context);
}
