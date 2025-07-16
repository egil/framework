namespace Egil.Orleans.EventSourcing.Handlers;

public interface IEventHandler<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull
{
    ValueTask<TProjection> HandleAsync(TEvent @event, TProjection projection, IEventHandlerContext context);
}   