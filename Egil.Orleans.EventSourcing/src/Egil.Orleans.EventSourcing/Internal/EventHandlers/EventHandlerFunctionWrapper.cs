namespace Egil.Orleans.EventSourcing.Internal.EventHandlers;

internal class EventHandlerFunctionWrapper<TEvent, TProjection> : IEventHandler<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly Func<TEvent, TProjection, IEventGrainContext, ValueTask<TProjection>> handlerFunction;

    public EventHandlerFunctionWrapper(Func<TEvent, TProjection, TProjection> handlerFunction)
        : this((e, p, c) => ValueTask.FromResult(handlerFunction(e, p)))
    {
    }

    public EventHandlerFunctionWrapper(Func<TEvent, TProjection, IEventGrainContext, ValueTask<TProjection>> handlerFunction)
    {
        this.handlerFunction = handlerFunction;
    }

    public ValueTask<TProjection> HandleAsync(TEvent @event, TProjection projection, IEventGrainContext context)
        => handlerFunction(@event, projection, context);
}
