using Egil.Orleans.EventSourcing.EventStores;

namespace Egil.Orleans.EventSourcing.EventHandlers;

internal class EventHandlerFunctionWrapper<TEvent, TProjection> : IEventHandler<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
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

    public ValueTask<TProjection> HandleAsync<TRequestedEvent>(TRequestedEvent @event, TProjection projection, IEventHandlerContext context) where TRequestedEvent : notnull
    {
        if (@event is TEvent castEvent)
        {
            return handlerFunction.Invoke(castEvent, projection, context); 
        }

        return ValueTask.FromResult(projection);
    }
}
