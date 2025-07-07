namespace Egil.Orleans.EventSourcing.Internal;

internal class EventPublisherFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, IEventPublisher<TEvent, TProjection>> publisherFactory) : IEventPublisherFactory<TEventGrain, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public IEventPublisher<TProjection> Create(TEventGrain grain, IServiceProvider serviceProvider)
        => EventPublisherWrapper<TEvent, TProjection>.Create(publisherFactory(grain));
}
