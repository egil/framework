using Egil.Orleans.EventSourcing.EventHandlers;

namespace Egil.Orleans.EventSourcing.EventHandlerFactories;

internal interface IEventHandlerFactory<TProjection>
    where TProjection : notnull
{
    IEventHandler<TProjection> Create();
}
