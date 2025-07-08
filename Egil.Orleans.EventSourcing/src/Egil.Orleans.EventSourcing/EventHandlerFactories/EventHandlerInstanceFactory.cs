namespace Egil.Orleans.EventSourcing.EventHandlerFactories;

internal class EventHandlerInstanceFactory<TEventGrain, TProjection>(IEventHandler<TProjection> handler) : IEventHandlerFactory<TEventGrain, TProjection>
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    public IEventHandler<TProjection> Create() => handler;
}
