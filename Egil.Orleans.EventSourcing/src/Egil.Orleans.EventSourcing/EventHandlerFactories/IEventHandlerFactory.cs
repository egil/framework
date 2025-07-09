using Orleans;

namespace Egil.Orleans.EventSourcing.EventHandlerFactories;

public interface IEventHandlerFactory<TEventGrain, TProjection>
    where TEventGrain : IGrainBase
    where TProjection : notnull
{
    IEventHandler<TProjection> Create();
}
