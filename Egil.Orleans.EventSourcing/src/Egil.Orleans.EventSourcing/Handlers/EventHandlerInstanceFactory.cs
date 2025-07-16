using Orleans;

namespace Egil.Orleans.EventSourcing.Handlers;

internal class EventHandlerInstanceFactory<TEventGrain, TEvent, TProjection>(IEventHandler<TEvent, TProjection> handler) : IEventHandlerFactory<TProjection>
    where TEventGrain : IGrainBase
    where TEvent : notnull
    where TProjection : notnull
{
    private IEventHandler<TProjection>? handlerWrapper;

    public IEventHandler<TProjection> Create()
    {
        handlerWrapper ??= new EventHandlerWrapper<TEvent, TProjection>(handler);
        return handlerWrapper;
    }
}
