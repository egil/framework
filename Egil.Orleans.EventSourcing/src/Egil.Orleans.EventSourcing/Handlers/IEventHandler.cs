namespace Egil.Orleans.EventSourcing.Handlers;

public interface IEventHandler<TProjection>
    where TProjection : notnull
{
    ValueTask<TProjection> HandleAsync<TEvent>(TEvent @event, TProjection projection, IEventHandlerContext context) where TEvent : notnull;
}
