namespace Egil.Orleans.EventSourcing.EventHandlerFactories;

internal class EventHandlerInstanceFactory<TEventGrain, TProjection>(IEventHandler<TProjection> handler) : IEventHandlerFactory<TEventGrain, TProjection>
    where TEventGrain : IGrainBase
    where TProjection : notnull, IEventProjection<TProjection>
{
    public IEventHandler<TProjection> Create() => handler;
}
