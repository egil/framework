using Orleans;

namespace Egil.Orleans.EventSourcing.Internal;

internal class EventHandlerFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, IEventHandler<TEvent, TProjection>> handlerFactory) : IEventHandlerFactory<TEventGrain>
    where TEventGrain : IGrain
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    public IEventHandler Create(TEventGrain grain)
    {
        return handlerFactory(grain);
    }
}
