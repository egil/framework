namespace Egil.Orleans.EventSourcing.EventHandlers;

public interface IEventHandler<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull
{
    ValueTask<TProjection> HandleAsync(TEvent @event, TProjection projection, IEventHandlerContext context);
}   