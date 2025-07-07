namespace Egil.Orleans.EventSourcing.Internal;

internal class EventPublisherWrapper<TEvent, TProjection> : IEventPublisher<TEvent, TProjection>, IEventPublisher<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{

    private readonly IEventPublisher<TEvent, TProjection> publisher;

    private EventPublisherWrapper(IEventPublisher<TEvent, TProjection> publisher)
    {
        this.publisher = publisher;
    }

    public ValueTask PublishAsync(IEnumerable<TEvent> @event, TProjection projection, IEventGrainContext context)
        => publisher.PublishAsync(@event, projection, context);

    public IEventPublisher<TRequestedEvent, TProjection>? TryCast<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull
    {
        {
            if (@event is TEvent)
            {
                return (IEventPublisher<TRequestedEvent, TProjection>)this;
            }

            return null;
        }
    }

    public static IEventPublisher<TProjection> Create(IEventPublisher<TEvent, TProjection> publisher)
       => new EventPublisherWrapper<TEvent, TProjection>(publisher);
}