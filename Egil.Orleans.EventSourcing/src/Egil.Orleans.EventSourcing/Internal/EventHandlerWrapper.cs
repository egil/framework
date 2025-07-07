namespace Egil.Orleans.EventSourcing.Internal;

internal class EventHandlerWrapper<TEvent, TProjection> : IEventHandler<TEvent, TProjection>, IEventHandler<TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventHandler<TEvent, TProjection> handler;

    private EventHandlerWrapper(IEventHandler<TEvent, TProjection> handler)
    {
        this.handler = handler;
    }

    public ValueTask<TProjection> HandleAsync(TEvent @event, TProjection projection, IEventGrainContext context)
        => handler.HandleAsync(@event, projection, context);

    public IEventHandler<TRequestedEvent, TProjection>? TryCast<TRequestedEvent>(TRequestedEvent @event) where TRequestedEvent : notnull
    {
        if (@event is TEvent)
        {
            return (IEventHandler<TRequestedEvent, TProjection>)this;
        }

        return null;
    }

    public static IEventHandler<TProjection> Create(IEventHandler<TEvent, TProjection> handler)
        => new EventHandlerWrapper<TEvent, TProjection>(handler);

    internal static IEventHandler<TProjection> Create(Func<TEvent, TProjection, TProjection> func)
        => new EventHandlerWrapper<TEvent, TProjection>(
            new EventHandlerFunctionWrapper<TEvent, TProjection>((e, p, c) => ValueTask.FromResult(func(e, p))));
}
