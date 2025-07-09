using Egil.Orleans.EventSourcing.EventReactors;

namespace Egil.Orleans.EventSourcing.EventReactorFactories;

internal class EventReactorFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, IEventReactor<TEvent, TProjection>> publisherFactory, TEventGrain eventGrain) : IEventReactorFactory<TEventGrain, TProjection>
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private IEventReactor<TProjection>? reactor;

    public IEventReactor<TProjection>? Create()
    {
        reactor ??= new EventReactorWrapper<TEvent, TProjection>(publisherFactory(eventGrain));
        return reactor;
    }
}
