using Egil.Orleans.EventSourcing.EventStores;

namespace Egil.Orleans.EventSourcing;

public interface IEventReactor<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull
{
    ValueTask ReactAsync(IEnumerable<TEvent> @event, TProjection projection, IEventReactContext context);
}
