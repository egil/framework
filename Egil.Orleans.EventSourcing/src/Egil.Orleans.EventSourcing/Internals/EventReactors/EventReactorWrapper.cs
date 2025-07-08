namespace Egil.Orleans.EventSourcing.Internals.EventReactors;

internal class EventReactorWrapper<TEvent, TProjection> : IEventReactor<TEvent, TProjection>, IEventReactor<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{

    private readonly IEventReactor<TEvent, TProjection> publisher;

    private EventReactorWrapper(IEventReactor<TEvent, TProjection> publisher)
    {
        this.publisher = publisher;
    }

    public ValueTask ReactAsync(IEnumerable<TEvent> @event, TProjection projection, IEventReactContext context)
        => publisher.ReactAsync(@event, projection, context);

    public IEventReactor<TRequestedEvent, TProjection>? TryCast<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull
    {
        {
            if (@event is TEvent)
            {
                return (IEventReactor<TRequestedEvent, TProjection>)this;
            }

            return null;
        }
    }

    public static IEventReactor<TProjection> Create(IEventReactor<TEvent, TProjection> publisher)
       => new EventReactorWrapper<TEvent, TProjection>(publisher);
}