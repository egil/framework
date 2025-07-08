using Egil.Orleans.EventSourcing.EventReactors;
using Orleans;

namespace Egil.Orleans.EventSourcing.EventReactorFactories;

internal class EventReactorFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, IEventReactor<TEvent, TProjection>> publisherFactory) : IEventReactorFactory<TEventGrain, TProjection>
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private IEventReactor<TProjection>? reactor;

    public IEventReactor<TProjection>? Create(TEventGrain grain, IServiceProvider serviceProvider)
    {
        reactor ??= EventReactorWrapper<TEvent, TProjection>.Create(publisherFactory(grain));
        return reactor;
    }

}
