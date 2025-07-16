namespace Egil.Orleans.EventSourcing.Handlers;

internal interface IEventHandlerFactory<TProjection>
    where TProjection : notnull
{
    IEventHandler<TProjection> Create();
}
