namespace Egil.Orleans.EventSourcing.Handlers;

internal class EventHandlerFunctionWrapper<TEvent, TProjection> : IEventHandler<TEvent, TProjection>, IEventHandler<TProjection>
    where TEvent : notnull
    where TProjection : notnull
{
    private readonly Func<TEvent, TProjection, IEventHandlerContext, ValueTask<TProjection>> handlerFunction;

    public EventHandlerFunctionWrapper(Func<TEvent, TProjection, TProjection> handlerFunction)
        : this((e, p, c) => ValueTask.FromResult(handlerFunction(e, p)))
    {
    }

    public EventHandlerFunctionWrapper(Func<TEvent, TProjection, IEventHandlerContext, ValueTask<TProjection>> handlerFunction)
    {
        this.handlerFunction = handlerFunction;
    }

    public ValueTask<TProjection> HandleAsync(TEvent @event, TProjection projection, IEventHandlerContext context)
        => handlerFunction.Invoke(@event, projection, context);

    public ValueTask<TProjection> HandleAsync<TRequestedEvent>(TRequestedEvent @event, TProjection projection, IEventHandlerContext context) where TRequestedEvent : notnull
    {
        if (@event is TEvent castEvent)
        {
            return HandleAsync(castEvent, projection, context); 
        }

        return ValueTask.FromResult(projection);
    }
}
