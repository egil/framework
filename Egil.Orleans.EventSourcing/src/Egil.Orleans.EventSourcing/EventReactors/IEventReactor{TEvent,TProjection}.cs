namespace Egil.Orleans.EventSourcing.EventReactors;

public interface IEventReactor<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull
{
    string Identifier { get; }

    ValueTask ReactAsync(IEnumerable<TEvent> @event, TProjection projection, IEventReactContext context);
}
