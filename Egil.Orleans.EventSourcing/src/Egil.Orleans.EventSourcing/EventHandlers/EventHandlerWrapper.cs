namespace Egil.Orleans.EventSourcing.EventHandlers;

internal class EventHandlerWrapper<TEvent, TProjection> : IEventHandler<TEvent, TProjection>, IEventHandler<TProjection>
    where TEvent : notnull
    where TProjection : notnull
{
    private readonly IEventHandler<TEvent, TProjection> handler;

    public EventHandlerWrapper(IEventHandler<TEvent, TProjection> handler)
    {
        this.handler = handler;
    }

    public ValueTask<TProjection> HandleAsync(TEvent @event, TProjection projection, IEventHandlerContext context)
        => handler.HandleAsync(@event, projection, context);

    ValueTask<TProjection> IEventHandler<TProjection>.HandleAsync<TSpecificEvent>(TSpecificEvent @event, TProjection projection, IEventHandlerContext context)
    {
        if (@event is TEvent castEvent)
        {
            return HandleAsync(castEvent, projection, context);
        }

        return ValueTask.FromResult(projection);
    }
}
