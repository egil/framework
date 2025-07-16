using Orleans;

namespace Egil.Orleans.EventSourcing.Reactors;

internal class EventReactorFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, IEventReactor<TEvent, TProjection>> publisherFactory, TEventGrain eventGrain, string identifier) : IEventReactorFactory<TProjection>
    where TEventGrain : IGrainBase
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private IEventReactor<TProjection>? reactor;

    public IEventReactor<TProjection> Create()
    {
        reactor ??= new EventReactorWrapper<TEvent, TProjection>(publisherFactory(eventGrain), identifier);
        return reactor;
    }
}
