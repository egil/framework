using Egil.Orleans.EventSourcing.EventHandlers;

namespace Egil.Orleans.EventSourcing.EventHandlerFactories;

internal class EventHandlerFactory<TEventGrain, TEvent, TProjection>(Func<TEventGrain, IEventHandler<TEvent, TProjection>> handlerFactory, TEventGrain eventGrain) : IEventHandlerFactory<TEventGrain, TProjection>
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
    where TEvent : notnull
{
    private IEventHandler<TProjection>? handler;

    public IEventHandler<TProjection> Create()
    {
        handler ??= new EventHandlerWrapper<TEvent, TProjection>(handlerFactory(eventGrain));
        return handler;
    }
}
