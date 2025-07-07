namespace Egil.Orleans.EventSourcing.Internal.EventHandlers;

internal class EventHandlerWrapper<TEvent, TProjection> : IEventHandler<TEvent, TProjection>, IEventHandler<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventHandler<TEvent, TProjection> handler;

    private EventHandlerWrapper(IEventHandler<TEvent, TProjection> handler)
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

        throw new InvalidOperationException($"Cannot handle event of type {typeof(TSpecificEvent)} with handler for {typeof(TEvent)}. This handler should never have been selected.");
    }

    public IEventHandler<TProjection>? TryCast<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull
    {
        if (@event is TEvent)
        {
            return this;
        }

        return null;
    }

    public static IEventHandler<TProjection> Create(IEventHandler<TEvent, TProjection> handler)
        => new EventHandlerWrapper<TEvent, TProjection>(handler);

    internal static IEventHandler<TProjection> Create(Func<TEvent, TProjection, TProjection> func)
        => new EventHandlerWrapper<TEvent, TProjection>(new EventHandlerFunctionWrapper<TEvent, TProjection>((e, p, c) => ValueTask.FromResult(func(e, p))));
}
