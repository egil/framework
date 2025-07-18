namespace Egil.Orleans.EventSourcing.Reactors;

public interface IEventReactor<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull
{
    string Id { get; }

    ValueTask ReactAsync(IEnumerable<TEvent> @event, TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default);
}
