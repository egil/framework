using Egil.Orleans.EventSourcing.EventHandlers;
using Orleans;

namespace Egil.Orleans.EventSourcing.EventHandlerFactories;

internal class EventHandlerFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, IEventHandler<TEvent, TProjection>> handlerFactory, TEventGrain eventGrain) : IEventHandlerFactory<TProjection>
    where TEventGrain : IGrainBase
    where TEvent : notnull
    where TProjection : notnull
{
    private IEventHandler<TProjection>? handler;

    public IEventHandler<TProjection> Create()
    {
        handler ??= new EventHandlerWrapper<TEvent, TProjection>(handlerFactory(eventGrain));
        return handler;
    }
}
