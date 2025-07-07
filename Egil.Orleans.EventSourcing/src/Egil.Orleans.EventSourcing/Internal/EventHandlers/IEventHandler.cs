namespace Egil.Orleans.EventSourcing.Internal.EventHandlers;

internal interface IEventHandler<TProjection> where TProjection : notnull, IEventProjection<TProjection>
{
    IEventHandler<TProjection>? TryCast<TEvent>(TEvent @event)
        where TEvent : notnull;

    ValueTask<TProjection> HandleAsync<TEvent>(TEvent @event, TProjection projection, IEventHandlerContext context) where TEvent : notnull;
}
