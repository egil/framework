namespace Egil.Orleans.EventSourcing.EventHandlerFactories;

public interface IEventHandlerFactory<TEventGrain, TProjection>
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    IEventHandler<TProjection> Create();
}
