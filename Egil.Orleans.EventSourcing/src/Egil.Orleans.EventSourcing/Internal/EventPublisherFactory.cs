using Orleans;

namespace Egil.Orleans.EventSourcing.Internal;

internal class EventPublisherFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, IEventPublisher<TEvent, TProjection>> publisherFactory) : IEventPublisherFactory<TEventGrain>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public IEventPublisher Create(TEventGrain grain, IServiceProvider serviceProvider)
    {
        return publisherFactory(grain);
    }
}
